const fs = require('fs');
const { BinaryTransferClient } = require('..');

async function uploadBuffer(filePath, bucketKey, objectKey, accessToken) {
    const client = new BinaryTransferClient(accessToken);
    const buffer = fs.readFileSync(filePath);
    const object = await client.uploadObject(bucketKey, objectKey, buffer);
    return object;
}

if (process.argv.length < 6) {
    console.log('Usage:');
    console.log('node ' + __filename + ' <path to local file> <bucket key> <object key> <access token>');
    process.exit(0);
}

uploadBuffer(process.argv[2], process.argv[3], process.argv[4], process.argv[5])
    .then(obj => console.log(obj))
    .catch(err => console.error(err));