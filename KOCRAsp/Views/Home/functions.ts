function safeParseJson(jsonString: string): any[] {
    if (!jsonString) return [];

    try {
        return JSON.parse(jsonString);
    }
    catch (e) {
        console.error("Failed to parse JSON:", jsonString);
        return [];
    }
}

var readFileEntry = async function(entry: any): Promise<any> {
    return new Promise(resolve => {
        entry.file((file: any) => {
            file.relativePath = entry.fullPath;
            resolve(file);
        });
    });
};

var readAllDirectoryEntries = async function(directoryReader: any): Promise<any[]> 
{
    const entries: any[] = [];
    let readEntries = await new Promise<any[]>(resolve => directoryReader.readEntries(resolve));
    while (readEntries.length > 0) 
    {
        entries.push(...readEntries);
        readEntries = await new Promise<any[]>(resolve => directoryReader.readEntries(resolve));
    }
    return entries;
};

var getAllFileEntries = async function(dataTransferItemList: any): Promise<any[]> 
{
    const fileEntries: any[] = [];
    const queue: any[] = [];

    for (let i = 0; i < dataTransferItemList.length; i++) 
    {
        const entry = dataTransferItemList[i].webkitGetAsEntry();
        if (entry) queue.push(entry);
    }

    while (queue.length > 0) 
    {
        const entry = queue.shift();
        if (entry.isFile) 
        {
            const file = await readFileEntry(entry);
            fileEntries.push(file);
        } 
        else if (entry.isDirectory) 
        {
            const reader = entry.createReader();
            const entries = await readAllDirectoryEntries(reader);
            queue.push(...entries);
        }
    }
    return fileEntries;
};

var renderDialogFileList = function(fileListEl: HTMLElement, uploadBtn: HTMLButtonElement, selectedFiles: any[]): void 
{
    fileListEl.innerHTML = '';

    if (selectedFiles.length === 0) 
    {
        uploadBtn.disabled = true;
        return;
    }

    uploadBtn.disabled = false;

    selectedFiles.forEach((file: any, index: number) => 
    {
        const div = document.createElement('div');
        div.style.cssText = 'display:flex; align-items:center; justify-content:space-between; padding:8px 12px; background:#f8f9fa; border-radius:6px;';

        const nameSpan = document.createElement('span');
        nameSpan.textContent = file.relativePath || file.name;
        nameSpan.style.cssText = 'flex:1; overflow:hidden; text-overflow:ellipsis; white-space:nowrap;';

        const removeBtn = document.createElement('button');
        removeBtn.textContent = '×';
        removeBtn.style.cssText = 'background:none; border:none; color:#dc3545; font-size:1.2rem; cursor:pointer; padding:0 8px;';
        removeBtn.onclick = () => 
        {
            selectedFiles.splice(index, 1);
            renderDialogFileList(fileListEl, uploadBtn, selectedFiles);
        };

        div.appendChild(nameSpan);
        div.appendChild(removeBtn);
        fileListEl.appendChild(div);
    });
};

var resetState = function(dropZone: HTMLElement, fileListEl: HTMLElement, uploadBtn: HTMLButtonElement, selectedFiles: any[]): void
{
    selectedFiles.length = 0;
    renderDialogFileList(fileListEl, uploadBtn, selectedFiles);
    dropZone.style.borderColor = '#ccc';
    dropZone.style.backgroundColor = '';
};

var closeDialog = function (dialog: HTMLDialogElement): void {
    dialog.close();
};

// ===== Ambient declarations for globals defined elsewhere (site.js / Index.cshtml / _Layout.cshtml) =====
declare function postForm(url: string, formData: FormData): Promise<any>;
declare function showToast(message: string, type?: string): void;
declare function showBetaDialog(message: string): void;
declare function showGuestOcrLimitModal(message: string, title: string): void;
declare function setBetaLimitStateFromServer(data: any): void;
declare function refreshFileList(page?: number): Promise<void>;
declare function refreshBatchDropdown(): Promise<void>;
declare function updateExportButtonState(): Promise<void>;
declare function showUploadOverlay(): void;
declare function hideUploadOverlay(): void;

declare var _isBetaTestOrganization: boolean;
declare var _betaLimitExceeded: boolean;
declare var _betaUsedPages: number;
declare var _betaMaxPages: number;
declare var _isGuestOrganization: boolean;
declare var _guestMaxInvoicesPerBatch: number;
declare var _guestMaxBatches: number;

var handleUploadClick = async function(dialog: HTMLDialogElement, uploadBtn: HTMLButtonElement, fileListEl: HTMLElement, selectedFiles: any[]): Promise<void>
{
    if (selectedFiles.length === 0) return;

    if (_isBetaTestOrganization && _betaLimitExceeded) 
    {
        showBetaDialog(`This beta-test organization has reached its OCR limit (${_betaUsedPages}/${_betaMaxPages} pages). Uploads and OCR are disabled.`);
        return;
    }

    uploadBtn.disabled = true;
    uploadBtn.textContent = 'Uploading...';

    const formData = new FormData();
    selectedFiles.forEach((file: any) => 
    {
        // Use relativePath if available (from folder drag)
        const filename = file.relativePath || file.name;
        formData.append('files', file, filename);
        formData.append('clientPaths', filename);
    });

    showUploadOverlay();
    try 
    {
        const result = await postForm('/home/uploadfiles', formData);
        setBetaLimitStateFromServer(result);
        if (result.success) 
        {
            showToast(`Uploaded ${result.filesUploaded} file(s).`);
            if (result.filesSkippedByLimit > 0) 
            {
                if (_isGuestOrganization) 
                {
                    showGuestOcrLimitModal(
                        `${result.filesSkippedByLimit} file(s) were not uploaded: the batch has reached its limit of ${_guestMaxInvoicesPerBatch} files. ` +
                        `Create a new batch to upload more (up to ${_guestMaxBatches} batches allowed).`,
                        'Guest Batch Limit Reached'
                    );
                } 
                else 
                {
                    showToast(`${result.filesSkippedByLimit} file(s) were not uploaded: batch invoice limit reached.`, 'warning');
                }
            }

            // Refresh UI to reflect uploaded files — go to first page so new uploads are visible
            await refreshFileList(1);
            await refreshBatchDropdown();
            await updateExportButtonState();

            // If server returned uploaded file names, select the first one in the file list
            try 
            {
                const uploadedNames = result.uploadedFileNames || [];
                if (uploadedNames.length > 0) 
                {
                    const target = uploadedNames[0];
                    const items = document.querySelectorAll('.file-list-item');
                    for (const item of items) 
                    {
                        const nameSpan = item.querySelector('.text-truncate');
                        if (nameSpan && nameSpan.textContent && nameSpan.textContent.trim() === target) 
                        {
                            (item as HTMLElement).click();
                            break;
                        }
                    }
                }
            } 
            catch (e) 
            {
                console.warn('Auto-select uploaded file failed:', e);
            }

            // Reset and close dialog
            selectedFiles.length = 0;
            renderDialogFileList(fileListEl, uploadBtn, selectedFiles);
            closeDialog(dialog);
        } 
        else 
        {
            if (result.betaLimitExceeded) 
            {
                showBetaDialog(result.error ?? `This beta-test organization has reached its OCR limit (${_betaUsedPages}/${_betaMaxPages} pages). Uploads and OCR are disabled.`);
            }
            showToast(result.error ?? 'Upload failed.', 'error');
        }
    } 
    catch (err: any) 
    {
        showToast('Upload error: ' + err.message, 'error');
        console.error(err);
    } 
    finally 
    {
        hideUploadOverlay();
        uploadBtn.disabled = false;
        uploadBtn.textContent = 'Upload';
    }
};
