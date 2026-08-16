namespace Il2CppDumper.Application;

internal static class Il2CppBinaryFactory
{
    public static Il2Cpp Create(byte[] bytes, int machoIndex = 0)
    {
        if (bytes.Length < sizeof(uint))
            throw new InvalidDataException("The executable file is empty or truncated.");

        var magic = BitConverter.ToUInt32(bytes, 0);
        var stream = new MemoryStream(bytes, writable: true);
        return magic switch
        {
            0x6D736100 => new WebAssembly(stream).CreateMemory(),
            0x304F534E => new NSO(stream).UnCompress(),
            0x905A4D => new PE(stream),
            0x464C457F when bytes[4] == 2 => new Elf64(stream),
            0x464C457F => new Elf(stream),
            0xFEEDFACF => new Macho64(stream),
            0xFEEDFACE => new Macho(stream),
            0xCAFEBABE or 0xBEBAFECA => CreateFatMacho(bytes, machoIndex),
            _ => throw new NotSupportedException("The executable format is not supported.")
        };
    }

    private static Il2Cpp CreateFatMacho(byte[] bytes, int requestedIndex)
    {
        var fat = new MachoFat(new MemoryStream(bytes));
        var index = Math.Clamp(requestedIndex, 0, fat.fats.Length - 1);
        var slice = fat.GetMacho(index);
        return fat.fats[index].magic == 0xFEEDFACF
            ? new Macho64(new MemoryStream(slice))
            : new Macho(new MemoryStream(slice));
    }
}
