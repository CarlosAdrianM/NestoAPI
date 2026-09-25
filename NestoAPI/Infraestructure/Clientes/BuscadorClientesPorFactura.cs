using NestoAPI.Models;
using System.Linq;
using System.Text.RegularExpressions;

namespace NestoAPI.Infraestructure.Clientes
{
    /// <summary>
    /// NestoAPI#544 (f): cuando la búsqueda de clientes no encuentra nada y lo que se ha tecleado
    /// tiene formato de número de factura (2 letras de serie + 2 dígitos de año + 5 de contador,
    /// 9 caracteres: NV2615541), se busca la factura en CabFacturaVta y se devuelve su cliente y
    /// contacto. Así, con el número de factura que viene en la transferencia, administración va
    /// directa a la ficha para contabilizar el pago. Solo se paga cuando la búsqueda normal no da nada.
    ///
    /// La serie no se contrasta con la tabla Series: si la factura existe en CabFacturaVta, su serie
    /// existe; y si no existe, es una única búsqueda por clave primaria (Empresa, Número).
    /// </summary>
    public static class BuscadorClientesPorFactura
    {
        private static readonly Regex FORMATO_FACTURA = new Regex(@"^[A-Za-z]{2}\d{7}$", RegexOptions.Compiled);

        public static bool TieneFormatoDeFactura(string filtro)
            => !string.IsNullOrWhiteSpace(filtro) && FORMATO_FACTURA.IsMatch(filtro.Trim());

        public static string NormalizarNumero(string filtro) => filtro?.Trim().ToUpperInvariant();

        /// <summary>
        /// El cliente de la factura, dentro de <paramref name="clientes"/> (que puede llevar ya el
        /// filtro por vendedor). Null si el filtro no tiene formato de factura o la factura no existe.
        /// </summary>
        public static IQueryable<Cliente> ClienteDeLaFactura(NVEntities db, IQueryable<Cliente> clientes, string empresa, string filtro)
        {
            if (!TieneFormatoDeFactura(filtro))
            {
                return null;
            }
            string numero = NormalizarNumero(filtro);
            var factura = db.CabsFacturasVtas
                .Where(f => f.Empresa == empresa && f.Número == numero)
                .Select(f => new { f.Nº_Cliente, f.Contacto })
                .FirstOrDefault();
            if (factura == null)
            {
                return null;
            }
            // Sin recortar: en SQL el char() compara sin relleno y en memoria los datos van sin él
            return clientes.Where(c => c.Empresa == empresa && c.Nº_Cliente == factura.Nº_Cliente && c.Contacto == factura.Contacto);
        }
    }
}
