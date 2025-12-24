import zipfile
import os

def zip_folder(folder_path, output_path):
    with zipfile.ZipFile(output_path, 'w', zipfile.ZIP_DEFLATED) as zipf:
        for root, dirs, files in os.walk(folder_path):
            for file in files:
                file_path = os.path.join(root, file)
                arcname = os.path.relpath(file_path, folder_path)
                print(f"Zipping: {arcname}")
                zipf.write(file_path, arcname)

if __name__ == "__main__":
    source = os.path.abspath("release/lf-windows")
    destination = os.path.abspath("release/lf-windows-portable.zip")
    
    if os.path.exists(destination):
        os.remove(destination)
        
    print(f"Source: {source}")
    print(f"Destination: {destination}")
    
    zip_folder(source, destination)
    print("Zip created successfully.")
