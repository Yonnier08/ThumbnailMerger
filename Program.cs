using System.Text;
using MikuMikuLibrary.Archives;
using MikuMikuLibrary.IO.Common;
using MikuMikuLibrary.Sprites;

if (args.Length < 3)
{
    Console.WriteLine("Uso: ThumbnailMerger <config.toml> <carpeta_mods> <carpeta_salida>");
    return 1;
}

string configPath = args[0];
string modsFolder = args[1];
string outputFolder = args[2];

Directory.CreateDirectory(outputFolder);

var priority = ReadPriority(configPath);
Console.WriteLine($"Mods en config.toml: {priority.Count}");

var merged = new SpriteSet();
int mergedCount = 0;

// Recorremos en orden INVERSO de prioridad: el que tiene mayor prioridad
// se procesa último, así sus nombres "ganan" si algún día hace falta desambiguar.
foreach (var modName in Enumerable.Reverse(priority))
{
    string modFolder = Path.Combine(modsFolder, modName);

    if (!IsModEnabled(modFolder))
    {
        Console.WriteLine($"Omitido (deshabilitado): {modName}");
        continue;
    }

    string farcPath = Path.Combine(modFolder, "rom", "2d", "spr_sel_pvtmb.farc");

    if (!File.Exists(farcPath))
        continue;

    Console.WriteLine($"Procesando: {modName}");

    try
    {
        var farc = new FarcArchive();
        farc.Load(farcPath);

        string? sprEntryName = farc.FileNames.FirstOrDefault(
            n => n.EndsWith(".spr", StringComparison.OrdinalIgnoreCase));

        // Esta versión del juego guarda el sprite set adentro con extensión .bin en vez de .spr.
        sprEntryName ??= farc.FileNames.FirstOrDefault(
            n => n.EndsWith(".bin", StringComparison.OrdinalIgnoreCase));

        // Último recurso: si el farc tiene un solo archivo adentro, es ese, sea cual sea su nombre.
        if (sprEntryName == null && farc.FileNames.Count() == 1)
            sprEntryName = farc.FileNames.First();

        if (sprEntryName == null)
        {
            Console.WriteLine($"  -> No se encontró .spr adentro de {farcPath}, se ignora.");
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

            // Si dos mods tienen un sprite con el MISMO nombre (mismo PV), gana el de
            // mayor prioridad (se procesa último, así que reemplaza). No renombramos:
            // el juego busca los sprites por su nombre exacto de siempre.
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
        Console.WriteLine($"  -> Error procesando {modName}: {ex.Message}");
    }
}

string outputFarcPath = Path.Combine(outputFolder, "spr_sel_pvtmb.farc");
string tempSprPath = Path.Combine(outputFolder, "_temp_spr_sel_pvtmb.bin");

merged.Save(tempSprPath);

var outFarc = new FarcArchive { IsCompressed = true };
outFarc.Add("spr_sel_pvtmb.bin", tempSprPath);
outFarc.Save(outputFarcPath);

File.Delete(tempSprPath);

Console.WriteLine($"Listo. {mergedCount} sprites combinados de {priority.Count} mods -> {outputFarcPath}");
Console.WriteLine("Ahora corré auto_creat_mod_spr_db.py sobre la carpeta de salida para generar mod_spr_db.bin.");

return 0;

// Lee el array "priority" del config.toml (formato simple de strings entre comillas).
static List<string> ReadPriority(string configPath)
{
    var result = new List<string>();
    var lines = File.ReadAllLines(configPath, Encoding.UTF8);
    bool inPriorityBlock = false;

    foreach (var rawLine in lines)
    {
        string line = rawLine.Trim();

        if (!inPriorityBlock)
        {
            if (line.StartsWith("priority") && line.Contains("["))
                inPriorityBlock = true;

            continue;
        }

        if (line.StartsWith("]"))
            break;

        int firstQuote = line.IndexOf('"');
        int lastQuote = line.LastIndexOf('"');

        if (firstQuote < 0 || lastQuote <= firstQuote)
            continue;

        string entry = line.Substring(firstQuote + 1, lastQuote - firstQuote - 1);
        result.Add(entry); // El "-" es parte del nombre real de la carpeta, no un marcador.
    }

    return result;
}

// El activado/desactivado real vive en el config.toml PROPIO de cada mod (enabled = true/false),
// no en el prefijo de la lista de prioridad global.
static bool IsModEnabled(string modFolder)
{
    string modConfigPath = Path.Combine(modFolder, "config.toml");

    if (!File.Exists(modConfigPath))
        return true; // Si no tiene config.toml propio, asumimos que está activo.

    foreach (var rawLine in File.ReadAllLines(modConfigPath, Encoding.UTF8))
    {
        string line = rawLine.Trim();

        if (!line.StartsWith("enabled"))
            continue;

        int eq = line.IndexOf('=');
        if (eq < 0)
            continue;

        string value = line[(eq + 1)..].Trim();
        return value.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    return true; // No se encontró el campo, asumimos activo.
}
