using Autodesk.Forge;
using Autodesk.Forge.Model;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Forge_Upload_DirectToS3.test
{
  public static class upload_to_docs
  {
    public static async Task<dynamic> UploadFile(string filePath, string projectId, string folderId, string fileName)
    {
      BinarytransferClient.CREDENTIAL = await BinarytransferClient.Get2LeggedTokenAsync(new Scope[] { Scope.DataRead, Scope.DataWrite, Scope.DataCreate });

      FileStream fileStream = new FileStream(filePath, FileMode.Open);

      // prepare storage
      ProjectsApi projectApi = new ProjectsApi();
      projectApi.Configuration.AccessToken = BinarytransferClient.CREDENTIAL.access_token;
      StorageRelationshipsTargetData storageRelData = new StorageRelationshipsTargetData(StorageRelationshipsTargetData.TypeEnum.Folders, folderId);
      CreateStorageDataRelationshipsTarget storageTarget = new CreateStorageDataRelationshipsTarget(storageRelData);
      CreateStorageDataRelationships storageRel = new CreateStorageDataRelationships(storageTarget);
      BaseAttributesExtensionObject attributes = new BaseAttributesExtensionObject(string.Empty, string.Empty, new JsonApiLink(string.Empty), null);
      CreateStorageDataAttributes storageAtt = new CreateStorageDataAttributes(fileName, attributes);
      CreateStorageData storageData = new CreateStorageData(CreateStorageData.TypeEnum.Objects, storageAtt, storageRel);
      CreateStorage storage = new CreateStorage(new JsonApiVersionJsonapi(JsonApiVersionJsonapi.VersionEnum._0), storageData);
      dynamic storageCreated = await projectApi.PostStorageAsync(projectId, storage);

      string[] storageIdParams = ((string)storageCreated.data.id).Split('/');
      string[] bucketKeyParams = storageIdParams[storageIdParams.Length - 2].Split(':');
      string bucketKey = bucketKeyParams[bucketKeyParams.Length - 1];
      string objectName = storageIdParams[storageIdParams.Length - 1];

      // upload the file/object, which will create a new object
      ObjectsApi objects = new ObjectsApi();
      objects.Configuration.AccessToken = BinarytransferClient.CREDENTIAL.access_token;

      //This is the only difference from the old method
      var response = await BinarytransferClient.UploadToBucket(bucketKey, objectName, fileStream);

      if ((int)response.StatusCode >= 400)
      {
        throw new Exception(response.Content);
      }

      // check if file already exists...
      FoldersApi folderApi = new FoldersApi();
      folderApi.Configuration.AccessToken = BinarytransferClient.CREDENTIAL.access_token;
      var filesInFolder = await folderApi.GetFolderContentsAsync(projectId, folderId);
      string itemId = string.Empty;
      foreach (KeyValuePair<string, dynamic> item in new DynamicDictionaryItems(filesInFolder.data))
        if (item.Value.attributes.displayName == fileName)
          itemId = item.Value.id; // this means a file with same name is already there, so we'll create a new version

      // now decide whether create a new item or new version
      if (string.IsNullOrWhiteSpace(itemId))
      {
        // create a new item
        BaseAttributesExtensionObject baseAttribute = new BaseAttributesExtensionObject(projectId.StartsWith("a.") ? "items:autodesk.core:File" : "items:autodesk.bim360:File", "1.0");
        CreateItemDataAttributes createItemAttributes = new CreateItemDataAttributes(fileName, baseAttribute);
        CreateItemDataRelationshipsTipData createItemRelationshipsTipData = new CreateItemDataRelationshipsTipData(CreateItemDataRelationshipsTipData.TypeEnum.Versions, CreateItemDataRelationshipsTipData.IdEnum._1);
        CreateItemDataRelationshipsTip createItemRelationshipsTip = new CreateItemDataRelationshipsTip(createItemRelationshipsTipData);
        StorageRelationshipsTargetData storageTargetData = new StorageRelationshipsTargetData(StorageRelationshipsTargetData.TypeEnum.Folders, folderId);
        CreateStorageDataRelationshipsTarget createStorageRelationshipTarget = new CreateStorageDataRelationshipsTarget(storageTargetData);
        CreateItemDataRelationships createItemDataRelationhips = new CreateItemDataRelationships(createItemRelationshipsTip, createStorageRelationshipTarget);
        CreateItemData createItemData = new CreateItemData(CreateItemData.TypeEnum.Items, createItemAttributes, createItemDataRelationhips);
        BaseAttributesExtensionObject baseAttExtensionObj = new BaseAttributesExtensionObject(projectId.StartsWith("a.") ? "versions:autodesk.core:File" : "versions:autodesk.bim360:File", "1.0");
        CreateStorageDataAttributes storageDataAtt = new CreateStorageDataAttributes(fileName, baseAttExtensionObj);
        CreateItemRelationshipsStorageData createItemRelationshipsStorageData = new CreateItemRelationshipsStorageData(CreateItemRelationshipsStorageData.TypeEnum.Objects, storageCreated.data.id);
        CreateItemRelationshipsStorage createItemRelationshipsStorage = new CreateItemRelationshipsStorage(createItemRelationshipsStorageData);
        CreateItemRelationships createItemRelationship = new CreateItemRelationships(createItemRelationshipsStorage);
        CreateItemIncluded includedVersion = new CreateItemIncluded(CreateItemIncluded.TypeEnum.Versions, CreateItemIncluded.IdEnum._1, storageDataAtt, createItemRelationship);
        CreateItem createItem = new CreateItem(new JsonApiVersionJsonapi(JsonApiVersionJsonapi.VersionEnum._0), createItemData, new List<CreateItemIncluded>() { includedVersion });

        ItemsApi itemsApi = new ItemsApi();
        itemsApi.Configuration.AccessToken = BinarytransferClient.CREDENTIAL.access_token;
        var newItem = await itemsApi.PostItemAsync(projectId, createItem);
        return newItem;
      }
      else
      {
        // create a new version
        BaseAttributesExtensionObject attExtensionObj = new BaseAttributesExtensionObject(projectId.StartsWith("a.") ? "versions:autodesk.core:File" : "versions:autodesk.bim360:File", "1.0");
        CreateStorageDataAttributes storageDataAtt = new CreateStorageDataAttributes(fileName, attExtensionObj);
        CreateVersionDataRelationshipsItemData dataRelationshipsItemData = new CreateVersionDataRelationshipsItemData(CreateVersionDataRelationshipsItemData.TypeEnum.Items, itemId);
        CreateVersionDataRelationshipsItem dataRelationshipsItem = new CreateVersionDataRelationshipsItem(dataRelationshipsItemData);
        CreateItemRelationshipsStorageData itemRelationshipsStorageData = new CreateItemRelationshipsStorageData(CreateItemRelationshipsStorageData.TypeEnum.Objects, storageCreated.data.id);
        CreateItemRelationshipsStorage itemRelationshipsStorage = new CreateItemRelationshipsStorage(itemRelationshipsStorageData);
        CreateVersionDataRelationships dataRelationships = new CreateVersionDataRelationships(dataRelationshipsItem, itemRelationshipsStorage);
        CreateVersionData versionData = new CreateVersionData(CreateVersionData.TypeEnum.Versions, storageDataAtt, dataRelationships);
        CreateVersion newVersionData = new CreateVersion(new JsonApiVersionJsonapi(JsonApiVersionJsonapi.VersionEnum._0), versionData);

        VersionsApi versionsApis = new VersionsApi();
        versionsApis.Configuration.AccessToken = BinarytransferClient.CREDENTIAL.access_token;
        dynamic newVersion = await versionsApis.PostVersionAsync(projectId, newVersionData);
        return newVersion;
      }
    }
  }
}