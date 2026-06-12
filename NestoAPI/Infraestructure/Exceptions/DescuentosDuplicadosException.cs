using System;

namespace NestoAPI.Infraestructure.Exceptions
{
    /// <summary>
    /// Issue #229: en DescuentosProducto solo puede haber una fila aplicable por filtro
    /// (cliente/familia/grupo/producto). Si hay más de una es un error de datos que debe
    /// corregir el usuario, así que el mensaje indica exactamente qué está duplicado.
    /// </summary>
    public class DescuentosDuplicadosException : NestoBusinessException
    {
        public DescuentosDuplicadosException(string ambito, string empresa, string cliente, Exception innerException)
            : base($"Hay descuentos duplicados para {ambito}. Por favor, corrija los descuentos duplicados e inténtelo de nuevo.",
                   new ErrorContext
                   {
                       ErrorCode = "DESCUENTOS_DUPLICADOS",
                       Empresa = empresa,
                       Cliente = cliente
                   },
                   innerException)
        {
        }
    }
}
