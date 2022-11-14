const axios = require('axios');
const rax = require('retry-axios');

class BinaryTransferClient {
    /**
     * Creates a new instance of the binary transfer helper client.
     *
     * Note that the provided access token will be used for all requests initiated
     * by this client. For long-running operations the token could potentially expire,
     * so consider modifying this class to refresh the token whenever needed.
     *
     * @param {string} token Access token to use when communicating with Autodesk Forge services.
     * @param {string} [host="https://developer.api.autodesk.com"] Optional Autodesk Forge host).
     */
    constructor(token, host) {
        this.token = token;
        this.axios = axios.create({
            baseURL: (host || 'https://developer.api.autodesk.com') + '/oss/v2/'
        });
        // Attach an interceptor to the axios instance that will retry response codes 100-199, 429, and 500-599.
        // For default settings, see https://github.com/JustinBeckwith/retry-axios#usage.
        this.axios.defaults.raxConfig = {
            instance: this.axios
        };
        rax.attach(this.axios);
    }

    /**
     * Generates one or more signed URLs that can be used to upload a file (or its parts) to OSS,
     * and an upload key that is used to generate additional URLs or in {@see _completeUpload}
     * after all the parts have been uploaded successfully.
     *
     * Note that if you are uploading in multiple parts, each part except for the final one
     * must be of size at least 5MB, otherwise the call to {@see _completeUpload} will fail.
     *
     * @async
     * @param {string} bucketKey Bucket key.
     * @param {string} objectKey Object key.
     * @param {number} [parts=1] How many URLs to generate in case of multi-part upload.
     * @param {number} [firstPart=1] Index of the part the first returned URL should point to.
     * For example, to upload parts 10 through 15 of a file, use `firstPart` = 10 and `parts` = 6.
     * @param {string} [uploadKey] Optional upload key if this is a continuation of a previously
     * initiated upload.
     * @param {number} [minutesExpiration] Custom expiration for the upload URLs
     * (within the 1 to 60 minutes range). If not specified, default is 2 minutes.
     * @returns {Promise<object>} Signed URLs for uploading chunks of the file to AWS S3,
     * and a unique upload key used to generate additional URLs or to complete the upload.
     */
    async _getUploadUrls(bucketKey, objectKey, parts = 1, firstPart = 1, uploadKey, minutesExpiration) {
        let endpoint = `buckets/${bucketKey}/objects/${encodeURIComponent(objectKey)}/signeds3upload?parts=${parts}&firstPart=${firstPart}`;
        if (uploadKey) {
            endpoint += `&uploadKey=${uploadKey}`;
        }
        if (minutesExpiration) {
            endpoint += `&minutesExpiration=${minutesExpiration}`;
        }
        const headers = {
            'Content-Type': 'application/json',
            'Authorization': 'Bearer ' + this.token
        };
        const resp = await this.axios.get(endpoint, { headers });
        return resp.data;
    }

    /**
     * Finalizes the upload of a file to OSS.
     *
     * @async
     * @param {string} bucketKey Bucket key.
     * @param {string} objectKey Object key.
     * @param {string} uploadKey Upload key returned by {@see _getUploadUrls}.
     * @param {string} [contentType] Optinal content type that should be recorded for the uploaded file.
     * @returns {Promise<object>} Details of the created object in OSS.
     */
    async _completeUpload(bucketKey, objectKey, uploadKey, contentType) {
        const endpoint = `buckets/${bucketKey}/objects/${encodeURIComponent(objectKey)}/signeds3upload`;
        const payload = { uploadKey };
        const headers = {
            'Content-Type': 'application/json',
            'Authorization': 'Bearer ' + this.token
        };
        if (contentType) {
            headers['x-ads-meta-Content-Type'] = contentType;
        }
        const resp = await this.axios.post(endpoint, payload, { headers });
        return resp.data;
    }

    /**
     * Uploads content to a specific bucket object.
     *
     * @async
     * @param {string} bucketKey Bucket key.
     * @param {string} objectKey Name of uploaded object.
     * @param {Buffer} data Object content.
     * @param {object} [options] Additional upload options.
     * @param {string} [options.contentType] Optional content type of the uploaded file.
     * @param {number} [options.minutesExpiration] Custom expiration for the upload URLs
     * (within the 1 to 60 minutes range). If not specified, default is 2 minutes.
     * @returns {Promise<object>} Object description containing 'bucketKey', 'objectKey', 'objectId',
     * 'sha1', 'size', 'location', and 'contentType'.
     * @throws Error when the request fails, for example, due to insufficient rights, or incorrect scopes.
     */
    async uploadObject(bucketKey, objectKey, data, options) {
        console.assert(data.byteLength > 0);
        const ChunkSize = 5 << 20;
        const MaxBatches = 25;
        const totalParts = Math.ceil(data.byteLength / ChunkSize);
        let partsUploaded = 0;
        let uploadUrls = [];
        let uploadKey;
        while (partsUploaded < totalParts) {
            const chunk = data.slice(partsUploaded * ChunkSize, Math.min((partsUploaded + 1) * ChunkSize, data.byteLength));
            while (true) {
                console.debug('Uploading part', partsUploaded + 1);
                if (uploadUrls.length === 0) {
                    // Automatically retries 429 and 500-599 responses
                    const uploadParams = await this._getUploadUrls(bucketKey, objectKey, Math.min(totalParts - partsUploaded, MaxBatches), partsUploaded + 1, uploadKey, options?.minutesExpiration);
                    uploadUrls = uploadParams.urls.slice();
                    uploadKey = uploadParams.uploadKey;
                }
                const url = uploadUrls.shift();
                try {
                    await this.axios.put(url, chunk);
                    break;
                } catch (err) {
                    const status = err.response?.status;
                    if (status === 403) {
                        console.debug('Got 403, refreshing upload URLs');
                        uploadUrls = []; // Couldn't this cause an infinite loop? (i.e., could the server keep responding with 403 indefinitely?)
                    } else {
                        throw err;
                    }
                }
            }
            console.debug('Part successfully uploaded', partsUploaded + 1);
            partsUploaded++;
        }
        console.debug('Completing part upload');
        return this._completeUpload(bucketKey, objectKey, uploadKey, options?.contentType);
    }

    /**
     * Uploads content stream to a specific bucket object.
     *
     * @async
     * @param {string} bucketKey Bucket key.
     * @param {string} objectKey Name of uploaded object.
     * @param {AsyncIterable<Buffer>} stream Input stream.
     * @param {object} [options] Additional upload options.
     * @param {string} [options.contentType] Optional content type of the uploaded file.
     * @param {number} [options.minutesExpiration] Custom expiration for the upload URLs
     * (within the 1 to 60 minutes range). If not specified, default is 2 minutes.
     * @returns {Promise<object>} Object description containing 'bucketKey', 'objectKey', 'objectId',
     * 'sha1', 'size', 'location', and 'contentType'.
     * @throws Error when the request fails, for example, due to insufficient rights, or incorrect scopes.
     */
    async uploadObjectStream(bucketKey, objectKey, input, options) {
        // Helper async generator making sure that each chunk has at least certain number of bytes
        async function* bufferChunks(input, minChunkSize) {
            let buffer = Buffer.alloc(2 * minChunkSize);
            let bytesRead = 0;
            for await (const chunk of input) {
                chunk.copy(buffer, bytesRead);
                bytesRead += chunk.byteLength;
                if (bytesRead >= minChunkSize) {
                    yield buffer.slice(0, bytesRead);
                    bytesRead = 0;
                }
            }
            if (bytesRead > 0) {
                yield buffer.slice(0, bytesRead);
            }
        }

        const MaxBatches = 25;
        const ChunkSize = 5 << 20;
        let partsUploaded = 0;
        let uploadUrls = [];
        let uploadKey;
        for await (const chunk of bufferChunks(input, ChunkSize)) {
            while (true) {
                console.debug('Uploading part', partsUploaded + 1);
                if (uploadUrls.length === 0) {
                    const uploadParams = await this._getUploadUrls(bucketKey, objectKey, MaxBatches, partsUploaded + 1, uploadKey, options?.minutesExpiration);
                    uploadUrls = uploadParams.urls.slice();
                    uploadKey = uploadParams.uploadKey;
                }
                const url = uploadUrls.shift();
                try {
                    await this.axios.put(url, chunk);
                    break;
                } catch (err) {
                    const status = err.response?.status;
                    if (status === 403) {
                        console.debug('Got 403, refreshing upload URLs');
                        uploadUrls = []; // Couldn't this cause an infinite loop? (i.e., could the server keep responding with 403 indefinitely?
                    } else {
                        throw err;
                    }
                }
            }
            console.debug('Part successfully uploaded', partsUploaded + 1);
            partsUploaded++;
        }
        console.debug('Completing part upload');
        return this._completeUpload(bucketKey, objectKey, uploadKey, options?.contentType);
    }

    /**
     * Generates a signed URL that can be used to download a file from OSS.
     *
     * @async
     * @param {string} bucketKey Bucket key.
     * @param {string} objectKey Object key.
     * @param {number} [minutesExpiration] Custom expiration for the download URLs
     * (within the 1 to 60 minutes range). If not specified, default is 2 minutes.
     * @returns {Promise<object>} Download URLs and potentially other helpful information.
     */
    async _getDownloadUrl(bucketKey, objectKey, minutesExpiration) {
        let endpoint = `buckets/${bucketKey}/objects/${encodeURIComponent(objectKey)}/signeds3download`;
        if (minutesExpiration) {
            endpoint += `?minutesExpiration=${minutesExpiration}`;
        }
        const headers = {
            'Content-Type': 'application/json',
            'Authorization': 'Bearer ' + this.token
        };
        const resp = await this.axios.get(endpoint, { headers });
        return resp.data;
    }

    /**
     * Downloads a specific OSS object.
     *
     * @async
     * @param {string} bucketKey Bucket key.
     * @param {string} objectKey Object key.
     * @param {object} [options] Additional download options.
     * @param {number} [options.minutesExpiration] Custom expiration for the download URLs
     * (within the 1 to 60 minutes range). If not specified, default is 2 minutes.
     * @returns {Promise<ArrayBuffer>} Object content.
     * @throws Error when the request fails, for example, due to insufficient rights, or incorrect scopes.
     */
    async downloadObject(bucketKey, objectKey, options) {
        console.debug('Retrieving download URL');
        const downloadParams = await this._getDownloadUrl(bucketKey, objectKey, options?.minutesExpiration);
        if (downloadParams.status !== 'complete') {
            throw new Error('File not available for download yet.');
        }
        const resp = await this.axios.get(downloadParams.url, {
            responseType: 'arraybuffer',
            onDownloadProgress: progressEvent => {
                const downloadedBytes = progressEvent.currentTarget.response.length;
                const totalBytes = parseInt(progressEvent.currentTarget.responseHeaders['Content-Length']);
                console.debug('Downloaded', downloadedBytes, 'bytes of', totalBytes);
            }
        });
        return resp.data;
    }

    /**
     * Downloads content stream of a specific bucket object.
     *
     * @async
     * @param {string} bucketKey Bucket key.
     * @param {string} objectKey Object name.
     * @param {object} [options] Additional download options.
     * @param {number} [options.minutesExpiration] Custom expiration for the download URLs
     * (within the 1 to 60 minutes range). If not specified, default is 2 minutes.
     * @returns {Promise<ReadableStream>} Object content stream.
     * @throws Error when the request fails, for example, due to insufficient rights, or incorrect scopes.
     */
    async downloadObjectStream(bucketKey, objectKey, options) {
        console.debug('Retrieving download URL');
        const downloadParams = await this._getDownloadUrl(bucketKey, objectKey, options?.minutesExpiration);
        if (downloadParams.status !== 'complete') {
            throw new Error('File not available for download yet.');
        }
        const resp = await this.axios.get(downloadParams.url, {
            responseType: 'stream',
            onDownloadProgress: progressEvent => {
                const downloadedBytes = progressEvent.currentTarget.response.length;
                const totalBytes = parseInt(progressEvent.currentTarget.responseHeaders['Content-Length']);
                console.debug('Downloaded', downloadedBytes, 'bytes of', totalBytes);
            }
        });
        return resp.data;
    }
}

module.exports = {
    BinaryTransferClient
};