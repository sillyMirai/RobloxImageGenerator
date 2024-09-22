import torch
from diffusers import StableDiffusionPipeline, EulerAncestralDiscreteScheduler, AutoencoderKL
from diffusers.pipelines.stable_diffusion import StableDiffusionSafetyChecker
from DeepCache import DeepCacheSDHelper
from compel import Compel
import sys, os, re, json

script_directory = os.path.dirname(os.path.realpath(__file__))
modelsFolder = os.path.join(script_directory, "Models")

##################
## Prompt collection for different styles

target_negative_prompts = {
   "anime": "(worst quality, low quality, score_1, score_2, score_3, score_4, score_5, score_6)1.3, (lowres, blurry, jpeg artifacts, extra digit, fewer digits, poorly drawn, cropped image)1.2, monochrome, dehydrated, bad anatomy, bad proportions, malformed limbs, mutated, deformed, disfigured, ugly, extra head, duplicate, extra fingers, disconnected fingers, deformed fingers, fused fingers, bad hands, mutated hands, watermark, artist signature",

   "person": "(worst quality, low quality, score_1, score_2, score_3, score_4, score_5, score_6)1.3, (monochrome, grayscale, lowres, blurry, jpeg artifacts, extra digit, fewer digits, poorly drawn, simple background, oversaturated, underexposed, cropped image)1.2, bad anatomy, bad proportions, extra limbs, malformed limbs, mutated, deformed, disfigured, ugly, extra fingers, disconnected fingers, deformed fingers, fused fingers, bad hands, mutated hands, watermark, artist signature",

   "other": "(worst quality, low quality, score_1, score_2, score_3, score_4, score_5, score_6)1.3, (monochrome, grayscale, lowres, blurry, jpeg artifacts, extra digit, fewer digits, poorly drawn, simple background, oversaturated, underexposed, cropped image)1.2"
}

target_prompts = {
   "anime": "(score_9, score_8_up, score_7_up, masterpiece, best quality, high quality, ultra highres)1.1, extremely detailed face, detailed facial features, soft lighting, sharp focus, correct anatomy",

   "person": "(score_9, score_8_up, score_7_up, masterpiece, best quality, high quality, ultra highres, ultra-detailed)1.1, extremely detailed face, detailed facial features, soft lighting, sharp focus, correct anatomy",

   "other": "(score_9, score_8_up, score_7_up, masterpiece, best quality, high quality, ultra highres, ultra-detailed)1.1, soft lighting, sharp focus, cinematic, realistic, volumetric dtx, HDR, ue5, octane render engine"
}

##################

def classifyPrompt(text, model):
    anime_models = ["hassaku"]  
    
    if model in anime_models:
        return "anime"

    pattern = r"\b(?:man|person|girl|guy|people|solo)\b"

    # Find all matches of trigger words in the text
    matches = re.findall(pattern, text, re.IGNORECASE)

    # Check if any standalone trigger words are present in the text
    if matches:
        return "person"
    else:
        return "other"


def runModel(prompt, negative_prompt, modelName, width, height, seed, num_images):
    try:    
        if seed > 0:
            torch.manual_seed(seed)

        # Load the variational encoder
        vae = AutoencoderKL.from_pretrained("LittleApple-fp16/vae-ft-mse-840000-ema-pruned", torch_dtype=torch.float16)
        
        # Load the safety variational encoder
        safety_checker = StableDiffusionSafetyChecker.from_pretrained("CompVis/stable-diffusion-safety-checker", torch_dtype=torch.float16)
        
        # Load the pipeline
        pipe = StableDiffusionPipeline.from_single_file(f"{modelsFolder}/Checkpoints/{modelName}.safetensors", 
                                                        vae=vae, 
                                                        safety_checker=safety_checker, 
                                                        torch_dtype=torch.float16).to("cuda")
        pipe.scheduler = EulerAncestralDiscreteScheduler.from_config(pipe.scheduler.config)

        # Enable xformers optimizations        
        pipe.enable_xformers_memory_efficient_attention()    
        pipe.vae.enable_xformers_memory_efficient_attention()    

        # Load deepcache helper
        helper = DeepCacheSDHelper(pipe=pipe)
        helper.set_params(cache_interval=2, cache_branch_id=1)
        helper.enable()

        target = classifyPrompt(prompt, modelName)
        targetEmbeddings = f"{modelsFolder}\\Embeddings\\{target}"

        # Load Embeddings
        for filename in os.listdir(targetEmbeddings):
            file_path = os.path.join(targetEmbeddings, filename)
            pipe.load_textual_inversion(file_path, weight_name=filename)

        compel_proc = Compel(tokenizer=pipe.tokenizer, text_encoder=pipe.text_encoder)

        # Process prompt and negative prompt embeddings
        prompt_embeds = compel_proc(f"{target_prompts[target]}, {prompt}").to("cuda").to(torch.float16)
        negative_prompt_embeds = compel_proc(f"{target_negative_prompts[target]}, {negative_prompt}").to("cuda").to(torch.float16)

        # Use pipeline to generate images
        with torch.inference_mode():
            result = pipe(
                prompt_embeds=prompt_embeds,
                negative_prompt_embeds=negative_prompt_embeds,
                num_inference_steps=20,
                width=width,
                height=height,
                guidance_scale=7,
                num_images_per_prompt=num_images,
                cross_attention_kwargs={"scale": 0.7}
            )

        # Convert images into bitwise operators
        images_data = []
        for i in range(num_images):
            image = result.images[i] 
            nsfw_check = result.nsfw_content_detected[i] 

            pixels = image.load()
            width, height = image.size

            # Prepare pixel data as packed integers
            pixel_data_packed = []
            for y in range(height):
                for x in range(width):
                    r, g, b = pixels[x, y]
                    packed_value = (r << 16) | (g << 8) | b
                    pixel_data_packed.append(packed_value)

            images_data.append({
                "Pixels": pixel_data_packed,
                "IsNSFW": nsfw_check
            })

        # Write output into buffer
        output_json = json.dumps(images_data)
        sys.stdout.write(output_json)
        sys.stdout.flush()

        # Release memory
        del image
        torch.cuda.empty_cache()
    except Exception: # handles memory leaks
        torch.cuda.empty_cache()
        sys.exit(1)

if __name__ == "__main__":
    runModel(sys.argv[1], sys.argv[2], sys.argv[3], int(sys.argv[4]), int(sys.argv[5]), int(sys.argv[6]), int(sys.argv[7]))

