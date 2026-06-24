using System;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

public class SimpleProductOcrParser : MonoBehaviour
{
    public ProductScanResult ParseIntoResult(string rawText, ProductScanResult seed = null)
    {
        ProductScanResult r = seed ?? new ProductScanResult();
        r.rawOcrText = rawText;

        if (string.IsNullOrWhiteSpace(rawText))
            return r;

        string text = NormalizeText(rawText);
        if (string.IsNullOrWhiteSpace(text))
            return r;

        if (string.IsNullOrWhiteSpace(r.quantity))
            r.quantity = ExtractQuantity(text);

        if (string.IsNullOrWhiteSpace(r.expiryDate))
            r.expiryDate = ExtractExpiry(text);

        if (string.IsNullOrWhiteSpace(r.ingredients))
            r.ingredients = ExtractIngredients(text);

        if (string.IsNullOrWhiteSpace(r.allergens))
            r.allergens = ExtractAllergens(text);

        if (string.IsNullOrWhiteSpace(r.productName))
            r.productName = ExtractBestProductName(text);

        return r;
    }

    public string ExtractQuantity(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        MatchCollection matches = Regex.Matches(
            text,
            @"\b\d+([.,]\d+)?\s?(g|kg|ml|l)\b",
            RegexOptions.IgnoreCase);

        foreach (Match m in matches)
        {
            if (m.Success)
                return m.Value.Trim();
        }

        Match labeled = Regex.Match(
            text,
            @"(cantitate\s*neta|cantitate\s*netă|gramaj|net|cantitate)\s*[:\-]?\s*(\d+([.,]\d+)?\s?(g|kg|ml|l))",
            RegexOptions.IgnoreCase);

        if (labeled.Success && labeled.Groups.Count > 2)
            return labeled.Groups[2].Value.Trim();

        return null;
    }

    public string ExtractExpiry(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        string[] patterns =
        {
            @"(expira|expirare|exp|best before|best by|a se consuma de preferinta inainte de|a se consuma înainte de|a se consuma inainte de|valabil pana la|valabil până la|valabil pana|valabil până)\s*[:\-]?\s*(\d{1,2}[./\-]\d{1,2}[./\-]\d{2,4})",
            @"(expira|expirare|exp|best before|best by|a se consuma de preferinta inainte de|a se consuma înainte de|a se consuma inainte de|valabil pana la|valabil până la|valabil pana|valabil până)\s*[:\-]?\s*(\d{1,2}[./\-]\d{2,4})",
            @"\b\d{1,2}[./\-]\d{1,2}[./\-]\d{2,4}\b",
            @"\b\d{1,2}[./\-]\d{2,4}\b"
        };

        foreach (string p in patterns)
        {
            Match m = Regex.Match(text, p, RegexOptions.IgnoreCase);
            if (m.Success)
            {
                if (m.Groups.Count > 2 && !string.IsNullOrWhiteSpace(m.Groups[2].Value))
                    return m.Groups[2].Value.Trim();

                return m.Value.Trim();
            }
        }

        return null;
    }

    public string ExtractIngredients(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        string normalized = NormalizeText(text);
        normalized = FixCommonOcrIngredientNoise(normalized);

        string lower = normalized.ToLowerInvariant();

        string[] markers =
        {
        "lista ingredientelor:",
        "lista ingredientelor",
        "ingrediente:",
        "ingredientele:",
        "ingrediente",
        "ingredientele",
        "ingredients:",
        "ingredients",
        "ingredient:",
        "ingredient",
        "compozitie:",
        "compoziție:",
        "compozitie",
        "compoziție",
        "ingr:"
    };

        int startIndex = -1;
        int markerLength = 0;

        foreach (string marker in markers)
        {
            int idx = lower.IndexOf(marker);
            if (idx >= 0)
            {
                startIndex = idx;
                markerLength = marker.Length;
                break;
            }
        }

        // Fallback pentru cazuri OCR: "INGR" sau "INGREDlENTE" pe linie separată.
        if (startIndex < 0)
        {
            Match m = Regex.Match(
                lower,
                @"\b(ingr|ingred[a-zăâîșț0-9]{2,15})\b\s*[:\-]?",
                RegexOptions.IgnoreCase);

            if (m.Success)
            {
                startIndex = m.Index;
                markerLength = m.Length;
            }
        }

        if (startIndex < 0)
            return null;

        int contentStart = startIndex + markerLength;

        while (contentStart < normalized.Length &&
               (normalized[contentStart] == ':' ||
                normalized[contentStart] == '-' ||
                normalized[contentStart] == '–' ||
                normalized[contentStart] == '—' ||
                normalized[contentStart] == ' '))
        {
            contentStart++;
        }

        string[] stopMarkers =
        {
        "valori nutritionale",
        "valori nutriționale",
        "informatii nutritionale",
        "informații nutriționale",
        "declarație nutrițională",
        "declaratie nutritionala",
        "nutritional values",
        "nutrition facts",
        "valoare energetica",
        "valoare energetică",
        "energie ",
        "per 100",
        "pentru 100",
        "depozitare",
        "mod de pastrare",
        "mod de păstrare",
        "a se pastra",
        "a se păstra",
        "conditii de pastrare",
        "condiții de păstrare",
        "lot:",
        "lot nr",
        "produs in",
        "produs în",
        "fabricat in",
        "fabricat în",
        "distribuit de",
        "importat de",
        "termen de valabilitate",
        "data expirarii",
        "data expirării",
        "best before",
        "expira",
        "expirare",
        "cod bare",
        "barcode"
    };

        int end = normalized.Length;

        foreach (string stop in stopMarkers)
        {
            int idx = lower.IndexOf(stop, contentStart);
            if (idx >= 0 && idx < end)
                end = idx;
        }

        // Nu tăiem ingredientele la "conține" / "poate conține".
        // Acestea apar des imediat după ingrediente și sunt utile pentru nevăzător.
        if (end <= contentStart)
            return null;

        string section = normalized.Substring(contentStart, end - contentStart).Trim();
        section = CleanSection(section);

        section = RemoveObviousNonIngredientTail(section);

        if (string.IsNullOrWhiteSpace(section) || section.Length < 4)
            return null;

        return Limit(section, 1200);
    }

    private string FixCommonOcrIngredientNoise(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        string s = text;

        // Corecții blânde, doar pentru marker, nu pentru tot conținutul.
        s = Regex.Replace(s, @"\bINGREDlENTE\b", "INGREDIENTE", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\bINGRED1ENTE\b", "INGREDIENTE", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\bINGREDlENTS\b", "INGREDIENTS", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\bINGRDIENTE\b", "INGREDIENTE", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\bINGEDIENTE\b", "INGREDIENTE", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\bINGREDIENTE\s*[-–—]\s*", "INGREDIENTE: ", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\bINGREDIENTS\s*[-–—]\s*", "INGREDIENTS: ", RegexOptions.IgnoreCase);

        return s;
    }

    private string RemoveObviousNonIngredientTail(string section)
    {
        if (string.IsNullOrWhiteSpace(section))
            return section;

        string s = section.Trim();

        // Dacă OCR-ul a prins foarte mult text după ingrediente, tăiem la indicii evidente.
        string[] hardStops =
        {
        " Valori nutritionale",
        " Valori nutriționale",
        " Informatii nutritionale",
        " Informații nutriționale",
        " Nutrition facts",
        " Nutritional values",
        " A se pastra",
        " A se păstra",
        " Mod de pastrare",
        " Mod de păstrare",
        " Lot:",
        " Expira",
        " Expirare",
        " Best before"
    };

        foreach (string stop in hardStops)
        {
            int idx = s.IndexOf(stop, StringComparison.OrdinalIgnoreCase);
            if (idx > 20)
            {
                s = s.Substring(0, idx).Trim();
                break;
            }
        }

        return s;
    }
    public string ExtractAllergens(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        string normalized = NormalizeText(text);
        string lower = normalized.ToLowerInvariant();

        // Caută mai întâi secțiunea explicită de alergeni
        string[] markers =
        {
        "alergeni:",
        "allergens:",
        "conține:",
        "contine:",
        "alergeni",
        "allergens",
        "conține urme de",
        "contine urme de",
        "poate conține urme de",
        "poate contine urme de",
        "conține",
        "contine",
        "poate contine",
        "poate conține",
    };

        foreach (string marker in markers)
        {
            int idx = lower.IndexOf(marker);
            if (idx < 0) continue;

            int start = idx + marker.Length;

            while (start < normalized.Length &&
                   (normalized[start] == ':' || normalized[start] == '-' || normalized[start] == ' '))
            {
                start++;
            }

            string[] stopMarkers =
            {
            "valori nutritionale",
            "valori nutriționale",
            "informatii nutritionale",
            "informații nutriționale",
            "depozitare",
            "mod de pastrare",
            "mod de păstrare",
            "expira",
            "expirare",
            "best before",
            "lot:",
            "lot nr",
            "termen",
            "nutrition facts",
            "nutritional values",
            "fabricat",
            "produs in",
            "produs în",
            "distribuit",
        };

            int end = normalized.Length;

            // Limităm la maxim 400 caractere după marker
            if (end - start > 400)
                end = start + 400;

            foreach (string stop in stopMarkers)
            {
                int p = lower.IndexOf(stop, start);
                if (p >= 0 && p < end)
                    end = p;
            }

            if (end > start)
            {
                string section = normalized.Substring(start, end - start).Trim();
                section = CleanSection(section);

                if (!string.IsNullOrWhiteSpace(section) && section.Length >= 3)
                    return Limit(section, 350);
            }
        }

        // Fallback: caută alergeni cunoscuți în textul brut
        string[] known =
        {
        "gluten", "grâu", "grau", "secară", "secara", "orz", "ovăz", "ovaz",
        "lapte", "lactoză", "lactoza",
        "ouă", "oua", "ou",
        "arahide",
        "soia",
        "nuci", "migdale", "alune de pădure", "alune de padure", "caju", "fistic", "nuci pecan",
        "pește", "peste",
        "crustacee",
        "moluște", "molusce",
        "țelină", "telina",
        "muștar", "mustar",
        "susan",
        "lupin",
        "sulfiți", "sulfiti", "dioxid de sulf",
    };

        var found = new System.Collections.Generic.List<string>();
        foreach (var a in known)
        {
            if (lower.Contains(a) && !found.Contains(a))
                found.Add(a);
        }

        return found.Count > 0 ? string.Join(", ", found) : null;
    }

    public string ExtractBestProductName(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        string[] lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        string best = null;
        int bestScore = int.MinValue;

        foreach (var line in lines)
        {
            string t = NormalizeSpaces(line);
            if (string.IsNullOrWhiteSpace(t)) continue;
            if (t.Length < 3) continue;
            if (t.Length > 90) continue;

            string low = t.ToLowerInvariant();

            if (low.Contains("ingrediente")) continue;
            if (low.Contains("ingredients")) continue;
            if (low.Contains("alergeni")) continue;
            if (low.Contains("allergens")) continue;
            if (low.Contains("valori nutritionale")) continue;
            if (low.Contains("valori nutriționale")) continue;
            if (low.Contains("informatii nutritionale")) continue;
            if (low.Contains("informații nutriționale")) continue;
            if (low.Contains("expira")) continue;
            if (low.Contains("expirare")) continue;
            if (low.Contains("lot")) continue;
            if (low.Contains("depozitare")) continue;
            if (low.Contains("mod de pastrare")) continue;
            if (low.Contains("mod de păstrare")) continue;
            if (low.Contains("www")) continue;
            if (low.Contains("http")) continue;
            if (low.Contains("ingred")) continue;
            if (low.Contains("alergen")) continue;
            if (low.Contains("nutrition")) continue;
            if (low.Contains("valoare")) continue;
            if (low.Contains("energie")) continue;
            if (low.Contains("kcal")) continue;
            if (low.Contains("kj")) continue;

            if (Regex.IsMatch(t, @"^\d+([.,]\d+)?\s?(g|kg|ml|l)$", RegexOptions.IgnoreCase))
                continue;

            int score = 0;

            if (t.Length >= 4 && t.Length <= 50) score += 30;
            if (Regex.IsMatch(t, @"[A-Za-zĂÂÎȘȚăâîșț]")) score += 20;
            if (!Regex.IsMatch(t, @"^\d")) score += 10;
            if (t.Split(' ').Length <= 7) score += 10;
            if (Regex.IsMatch(t, @"^[A-ZĂÂÎȘȚ0-9a-zăâîșț\s\-\,\.]+$")) score += 5;

            if (low.Contains("lapte")) score += 8;
            if (low.Contains("iaurt")) score += 8;
            if (low.Contains("biscuit")) score += 8;
            if (low.Contains("ciocol")) score += 8;
            if (low.Contains("apa")) score += 8;
            if (low.Contains("apă")) score += 8;
            if (low.Contains("suc")) score += 8;
            if (low.Contains("cafea")) score += 8;
            if (low.Contains("ceai")) score += 8;
            if (low.Contains("bio")) score += 5;

            if (score > bestScore)
            {
                bestScore = score;
                best = t;
            }
        }

        return Limit(best, 100);
    }

    private static string NormalizeText(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return s;

        s = s.Replace("\r\n", "\n");
        s = s.Replace("\r", "\n");
        s = s.Replace("\t", " ");

        while (s.Contains("  "))
            s = s.Replace("  ", " ");

        while (s.Contains("\n\n\n"))
            s = s.Replace("\n\n\n", "\n\n");

        return s.Trim();
    }

    private static string NormalizeSpaces(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return s;

        s = s.Replace("\t", " ");

        while (s.Contains("  "))
            s = s.Replace("  ", " ");

        return s.Trim();
    }

    private static string CleanSection(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return s;

        s = s.Trim();
        s = s.Trim(':', '-', '.', ',', ';', ' ');

        while (s.Contains("  "))
            s = s.Replace("  ", " ");

        s = s.Replace("\n", " ");
        s = s.Replace("\r", " ");

        while (s.Contains("  "))
            s = s.Replace("  ", " ");

        return s.Trim();
    }

    private static string Limit(string s, int max)
    {
        if (string.IsNullOrWhiteSpace(s))
            return s;

        if (s.Length <= max)
            return s;

        return s.Substring(0, max) + "...";
    }
}