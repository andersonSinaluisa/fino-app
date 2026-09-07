import { File } from 'expo-file-system';

export async function createUploadBlob(uri: string, mimeType: string): Promise<Blob> {
  const selectedFile = new File(uri);
  const bytes = await selectedFile.arrayBuffer();

  return new Blob([bytes], { type: mimeType });
}
