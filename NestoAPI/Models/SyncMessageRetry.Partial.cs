using NestoAPI.Models.Sincronizacion;
using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace NestoAPI.Models
{
    /// <summary>
    /// Clase parcial para agregar funcionalidad adicional a SyncMessageRetry generado por EDMX
    /// </summary>
    public partial class SyncMessageRetry
    {
        /// <summary>
        /// Estado convertido a enumeración para uso en código
        /// </summary>
        [NotMapped]
        public RetryStatus StatusEnum
        {
            get
            {
                if (Enum.TryParse<RetryStatus>(Status, out var result))
                {
                    return result;
                }
                return RetryStatus.Retrying;
            }
            set
            {
                Status = value.ToString();
            }
        }
    }
}
