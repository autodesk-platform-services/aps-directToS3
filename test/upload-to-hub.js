const fs = require('fs');
const path = require('path');
const { ProjectsApi, FoldersApi, ItemsApi, VersionsApi } = require('forge-apis');
const { BinaryTransferClient } = require('..');

async function getFolderContents(projectId, folderId, getAccessToken) {
    const resp = await new FoldersApi().getFolderContents(projectId, folderId, {}, null, getAccessToken());
    return resp.body.data;
}

async function createStorage(projectId, folderId, displayName, getAccessToken) {
    const body = {
        jsonapi: {
            version: '1.0'
        },
        data: {
            type: 'objects',
            attributes: {
                name: displayName
            },
            relationships: {
                target: {
                    data: {
                        type: 'folders',
                        id: folderId
                    }
                }
            }
        }
    };
    const resp = await new ProjectsApi().postStorage(projectId, body, null, getAccessToken());
    return resp.body.data;
}

async function createItem(projectId, folderId, objectId, displayName, getAccessToken) {
    const body = {
        jsonapi: {
            version: '1.0'
        },
        data: {
            type: 'items',
            attributes: {
                displayName,
                extension: {
                    type: 'items:autodesk.core:File',
                    version: '1.0'
                }
            },
            relationships: {
                tip: {
                    data: {
                        type: 'versions',
                        id: '1'
                    }
                },
                parent: {
                    data: {
                        type: 'folders',
                        id: folderId
                    }
                }
            }
        },
        included: [
            {
                type: 'versions',
                id: '1',
                attributes: {
                    name: displayName,
                    extension: {
                        type: 'versions:autodesk.core:File',
                        version: '1.0'
                    }
                },
                relationships: {
                    storage: {
                        data: {
                            type: 'objects',
                            id: objectId
                        }
                    }
                }
            }
        ]
    };
    const resp = await new ItemsApi().postItem(projectId, body, null, getAccessToken());
    return resp.body.data;
}

async function createVersion(projectId, lineageId, objectId, displayName, getAccessToken) {
    const body = {
        jsonapi: {
            version: '1.0'
        },
        data: {
            type: 'versions',
            attributes: {
                name: displayName,
                extension: {
                    type: 'versions:autodesk.core:File',
                    version: '1.0'
                }
            },
            relationships: {
                item: {
                    data: {
                        type: 'items',
                        id: lineageId
                    }
                },
                storage: {
                    data: {
                        type: 'objects',
                        id: objectId
                    }
                }
            }
        }
    };
    const resp = await new VersionsApi().postVersion(projectId, body, null, getAccessToken());
    return resp.body.data;
}

async function upload(filePath, projectId, folderId, accessToken) {
    const displayName = path.basename(filePath);
    const getAccessToken = () => {
        return { access_token: accessToken };
    };

    console.log('Creating storage...');
    const storage = await createStorage(projectId, folderId, displayName, getAccessToken);
    console.log(storage);
    const match = /urn:adsk.objects:os.object:([^\/]+)\/(.+)/.exec(storage.id);
    if (!match || match.length < 3) {
        throw new Error('Unexpected storage ID', storage.id);
    }
    const bucketKey = match[1];
    const objectKey = match[2];

    console.log('Uploading file...');
    const client = new BinaryTransferClient(accessToken);
    const object = await client.uploadObject(bucketKey, objectKey, fs.readFileSync(filePath));
    console.log(object);

    console.log('Checking if file already exists...');
    const contents = await getFolderContents(projectId, folderId, getAccessToken);
    const item = contents.find(e => e.type === 'items' && e.attributes.displayName === displayName);

    if (!item) {
        console.log('Creating new item...');
        const lineage = await createItem(projectId, folderId, object.objectId, displayName, getAccessToken);
        console.log(lineage);
    } else {
        console.log('Creating new item version...');
        const version = await createVersion(projectId, item.id, object.objectId, displayName, getAccessToken);
        console.log(version);
    }
}

if (process.argv.length < 6) {
    console.log('Usage:');
    console.log('node ' + __filename + ' <path to local file> <project id> <folder id> <access token>');
    process.exit(0);
}

upload(process.argv[2], process.argv[3], process.argv[4], process.argv[5])
    .then(obj => console.log('Done!'))
    .catch(err => console.error(err));