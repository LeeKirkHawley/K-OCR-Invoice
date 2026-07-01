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

