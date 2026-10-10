using Sntp.Adept.Domain.Models;

namespace Sntp.Adept.Excel;

public sealed class ImportProgress
{
    public int Percent { get; init; }
    public string Phase { get; init; } = "";
    public string Message { get; init; } = "";
    public string? Sheet { get; init; }
    public string? Form { get; init; }
    public int? SheetIndex { get; init; }
    public int? SheetCount { get; init; }

    public static string FormLabel(EstimateForm form) => form switch
    {
        EstimateForm.Form1Ps => "свод 1ПС",
        EstimateForm.Form2P707 => "позиции 2П",
        EstimateForm.Form2PSbc => "позиции 2П СБЦ",
        EstimateForm.AsutpCard => "карточка АСУТП",
        EstimateForm.Form3P => "форма 3П",
        EstimateForm.LaborCost => "форма 3П",
        _ => "нераспознанный лист"
    };
}
