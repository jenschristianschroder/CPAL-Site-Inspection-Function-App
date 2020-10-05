using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.jeschro
{
    public static class HttpTrigger_Base64ToBlob
    {
        private static string destinationContainer = null;
        private static CloudStorageAccount storageAccountDestination = null; 
        private static CloudBlobClient cloudBlobClientDestination = null;
        private static CloudBlobContainer cloudBlobContainer = null;
        private static byte[] imageBytes = null;
        private static CloudBlockBlob assetBlob = null;

        [FunctionName("HttpTrigger_Base64ToBlob")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            
            log.LogInformation(requestBody);

            string destinationStorageAccount = Environment.GetEnvironmentVariable("destinationStorageAccount");
            string destinationStorageKey = Environment.GetEnvironmentVariable("destinationStorageKey");
            destinationContainer = Environment.GetEnvironmentVariable("destinationContainer");

            storageAccountDestination = CloudStorageAccount.Parse("DefaultEndpointsProtocol=https;AccountName=" + destinationStorageAccount + ";AccountKey=" + destinationStorageKey + ";EndpointSuffix=core.windows.net");

            string inspection = JsonConvert.DeserializeObject<Inspection>(requestBody).Assets;
            List<Asset> assetCollection = JsonConvert.DeserializeObject<List<Asset>>(inspection);

            string assetCollectionName = assetCollection.Find(x => x.blob == null).identifier;

            //pass through assets and upload depending on type
            foreach(Asset asset in assetCollection) {
                switch (asset.description) {
                    case "audio input":
                        uploadBlob(assetCollectionName, asset, "audio", "data:audio/aac;base64,", "m4a");
                        break;
                    case "pen input":
                        uploadBlob(assetCollectionName, asset, "pen", "data:image/png;base64,", "png");
                        break;
                    case "photo input":
                        uploadBlob(assetCollectionName, asset, "photo", "data:image/jpeg;base64,", "jpg");
                        break;
                    case "measurement photo input":
                        uploadBlob(assetCollectionName, asset, "measurement photo", "data:image/jpeg;base64,", "jpg");
                        break;
                    case "measurement input":
                        uploadText(assetCollectionName, asset, "measurement");
                        break;
                    case "text input":
                        uploadText(assetCollectionName, asset, "text");
                        break;
                    default:
                        // upload complete payload to blob
                        cloudBlobClientDestination = storageAccountDestination.CreateCloudBlobClient();
                        cloudBlobContainer = cloudBlobClientDestination.GetContainerReference(destinationContainer);
                        assetBlob = cloudBlobContainer.GetBlockBlobReference(assetCollectionName + "/" + asset.identifier + "_evidence_collection.json");
                        await assetBlob.UploadTextAsync(requestBody);                            
                        break;
                }
                log.LogInformation(asset.description);
            }
            log.LogInformation("Asset extraction complete");
            string responseMessage = "Asset extraction complete";

            return new OkObjectResult(responseMessage);
        }

        // upload files to blob
        public static async void uploadBlob(string assetCollectionName, Asset asset, string type, string mimetype, string extension) {
            cloudBlobClientDestination = storageAccountDestination.CreateCloudBlobClient();
            cloudBlobContainer = cloudBlobClientDestination.GetContainerReference(destinationContainer);
            assetBlob = cloudBlobContainer.GetBlockBlobReference(assetCollectionName + "/" + asset.identifier + "_" + type + "_input." + extension);
            imageBytes = Convert.FromBase64String(asset.blobContent.Replace(mimetype, "").Replace("\"", ""));
            using(var stream = new MemoryStream(imageBytes, writable: false)) {
                stream.Position = 0;
                await assetBlob.UploadFromStreamAsync(stream);
                stream.Flush();
            }
            assetBlob = cloudBlobContainer.GetBlockBlobReference(assetCollectionName + "/" + asset.identifier + "_" + type + "_input_metadata.json");
            await assetBlob.UploadTextAsync(JsonConvert.SerializeObject(asset));
        }

        // upload text to blob
        public static async void uploadText(string assetCollectionName, Asset asset, string type) {
            cloudBlobClientDestination = storageAccountDestination.CreateCloudBlobClient();
            cloudBlobContainer = cloudBlobClientDestination.GetContainerReference(destinationContainer);
            assetBlob = cloudBlobContainer.GetBlockBlobReference(assetCollectionName + "/" + asset.identifier + "_" + type + "_input.json");
            string textJson = "{ \"" + type + "input\" : \"" + asset.blob + "\" }";
            await assetBlob.UploadTextAsync(textJson);
            assetBlob = cloudBlobContainer.GetBlockBlobReference(assetCollectionName + "/" + asset.identifier + "_" + type + "_input_metadata.json");
            await assetBlob.UploadTextAsync(JsonConvert.SerializeObject(asset));
        }
    }

    public class Inspection {
        public string Assets  {get;set;}

    }

    public class Asset {
        public float? altitude {get;set;}
        public float latitude {get;set;}
        public float longitude {get;set;}
        public string blob {get;set;}
        public string blobContent {get;set;}
        public string createdBy {get;set;}
        public string createdByName {get;set;}
        public DateTime createdOn {get;set;}
        public string description {get;set;}
        public string identifier {get;set;}
    }
}
