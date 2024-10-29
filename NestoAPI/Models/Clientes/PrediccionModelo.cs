using Microsoft.ML.Data;

namespace NestoAPI.Models.Clientes
{
    public class PrediccionModelo
    {
        [ColumnName("PredictedLabel")] // Si estás usando FastTree, esto sería la predicción binaria (0 o 1)
        public bool PredictedLabel { get; set; }

        [ColumnName("Probability")] // Esta es la probabilidad de que sea 'true'
        public float Probability { get; set; }

        [ColumnName("Score")] // El puntaje en algunos modelos, pero no siempre es una probabilidad
        public float Score { get; set; }
    }
}
