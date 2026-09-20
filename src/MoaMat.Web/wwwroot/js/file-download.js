// Hands a file generated in the browser (a PDF built by the WebAssembly app)
// to the user as a download. The bytes never leave the device.

/**
 * @param {string} fileName Name proposed to the user.
 * @param {string} contentType MIME type of the file.
 * @param {Uint8Array} bytes File content.
 */
export function downloadFile(fileName, contentType, bytes) {
    const blob = new Blob([bytes], { type: contentType });
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement('a');

    anchor.href = url;
    anchor.download = fileName;
    anchor.style.display = 'none';
    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();

    // Revoked on the next tick: revoking synchronously can cancel the download
    // in some browsers before it starts.
    setTimeout(() => URL.revokeObjectURL(url), 1000);
}
