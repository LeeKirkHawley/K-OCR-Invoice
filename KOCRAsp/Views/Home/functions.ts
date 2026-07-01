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

