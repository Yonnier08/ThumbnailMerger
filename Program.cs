using MikuMikuLibrary.Archives;
using MikuMikuLibrary.IO.Common;
using MikuMikuLibrary.Sprites;

if (args.Length < 2)
{
    Console.WriteLine("Uso: ThumbnailMerger <carpeta_con_farcs> <carpeta_salida_2d>");
    return 1;
}

string inputFolder = args[0];
string outputFolder = args[1];

if (!Directory.Exists(inputFolder))
{
    Console.WriteLine($"[ERROR] No existe la carpeta: {inputFolder}");
    return 1;
}

Directory.CreateDirectory(outputFolder);

// Orden alfabético = orden de prioridad (el último gana en caso de choque de nombres).
// Nombrá los archivos como spr_sel_pvtmb(1).farc, spr_sel_pvtmb(2).farc, etc. para controlar el orden.
var farcFiles = Directory.GetFiles(inputFolder, "*.farc")
    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
    .ToList();

Console.WriteLine($"Archivos .farc encontrados: {farcFiles.Count}");

var merged = new SpriteSet();
int mergedCount = 0;

foreach (var farcPath in farcFiles)
{
    Console.WriteLine($"Procesando: {Path.GetFileName(farcPath)}");

    try
    {
        var farc = new FarcArchive();
        farc.Load(farcPath);

        string? sprEntryName = farc.FileNames.FirstOrDefault(
            n => n.EndsWith(".spr", StringComparison.OrdinalIgnoreCase));

        sprEntryName ??= farc.FileNames.FirstOrDefault(
            n => n.EndsWith(".bin", StringComparison.OrdinalIgnoreCase));

        if (sprEntryName == null && farc.FileNames.Count() == 1)
            sprEntryName = farc.FileNames.First();

        if (sprEntryName == null)
        {
            Console.WriteLine($"  -> No se encontró el sprite set adentro, se ignora.");
            Console.WriteLine($"  -> Archivos reales adentro del farc: {string.Join(", ", farc.FileNames)}");
            continue;
        }

        using var entryStream = farc.Open(sprEntryName, EntryStreamMode.MemoryStream);

        var subSet = new SpriteSet();
        subSet.Load(entryStream, true);

        var indexMap = new Dictionary<int, int>();

        for (int i = 0; i < subSet.TextureSet.Textures.Count; i++)
        {
            var tex = subSet.TextureSet.Textures[i];
            int newIndex = merged.TextureSet.Textures.Count;
            merged.TextureSet.Textures.Add(tex);
            indexMap[i] = newIndex;
        }

        foreach (var spr in subSet.Sprites)
        {
            if (indexMap.TryGetValue((int)spr.TextureIndex, out int mappedIndex))
                spr.TextureIndex = (uint)mappedIndex;

            int existingIndex = merged.Sprites.FindIndex(s => s.Name == spr.Name);

            if (existingIndex >= 0)
                merged.Sprites[existingIndex] = spr;
            else
                merged.Sprites.Add(spr);

            mergedCount++;
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  -> Error procesando {Path.GetFileName(farcPath)}: {ex.Message}");
    }
}

string outputFarcPath = Path.Combine(outputFolder, "spr_sel_pvtmb.farc");
string tempSprPath = Path.Combine(outputFolder, "_temp_spr_sel_pvtmb.bin");

merged.Save(tempSprPath);

var outFarc = new FarcArchive { IsCompressed = true };
outFarc.Add("spr_sel_pvtmb.bin", tempSprPath);
outFarc.Save(outputFarcPath);

File.Delete(tempSprPath);

Console.WriteLine($"Listo. {mergedCount} sprites combinados de {farcFiles.Count} archivos -> {outputFarcPath}");
Console.WriteLine("Ahora corré auto_creat_mod_spr_db.py sobre esta carpeta para generar mod_spr_db.bin.");
