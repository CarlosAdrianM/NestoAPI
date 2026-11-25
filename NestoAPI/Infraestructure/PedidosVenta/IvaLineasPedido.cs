using NestoAPI.Models;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>
    /// Pedidos 929956, 931479, 931502, 931561 y 931592 (25/11/26): pedidos «sin IVA» (cabecera IVA NULL, se facturan por la
    /// empresa espejo) que quedaban A MEDIAS: unas líneas con PorcentajeIVA 21 e importe de IVA y otras a 0, y el reembolso y la
    /// factura salían de una mezcla. LA REGLA: el IVA de cada línea viva sale de la cabecera del pedido TAL COMO SE GUARDA; sin
    /// IVA en la cabecera, todo a 0 (porcentajes de IVA y RE, sus importes y Total = base). Lo usan el PUT al pasar a sin IVA y
    /// la última comprobación antes de guardar del POST y del PUT.
    /// </summary>
    public static class ReglaIvaLineasPedido
    {
        public static bool EsSinIva(string ivaCabecera) => string.IsNullOrWhiteSpace(ivaCabecera);

        /// <summary>Viva = la que aún se puede tocar y se va a servir: pendiente, en curso (-1..1).</summary>
        public static bool EsViva(LinPedidoVta linea) =>
            linea != null && linea.Estado >= Constantes.EstadosLineaVenta.PENDIENTE && linea.Estado <= Constantes.EstadosLineaVenta.EN_CURSO;

        /// <summary>La línea lleva algo de IVA o de recargo (porcentaje o importe), o su total no es su base.</summary>
        public static bool LlevaIva(LinPedidoVta linea) =>
            linea.PorcentajeIVA != 0 || linea.PorcentajeRE != 0 || linea.ImporteIVA != 0 || linea.ImporteRE != 0 ||
            linea.Total != linea.Base_Imponible;

        /// <summary>La línea sin IVA: porcentajes e importes de IVA y RE a 0 y Total = base. La base no cambia.</summary>
        public static void PonerSinIva(LinPedidoVta linea)
        {
            linea.PorcentajeIVA = 0;
            linea.PorcentajeRE = 0;
            linea.ImporteIVA = 0;
            linea.ImporteRE = 0;
            linea.Total = linea.Base_Imponible;
        }

        /// <summary>
        /// Con la cabecera sin IVA, deja sin IVA las líneas vivas que lo lleven y las devuelve (vacía si no había nada que tocar o
        /// la cabecera tiene IVA). Las servidas (albarán, factura) no se tocan.
        /// </summary>
        public static List<LinPedidoVta> NormalizarSiSinIva(string ivaCabecera, IEnumerable<LinPedidoVta> lineas)
        {
            var normalizadas = new List<LinPedidoVta>();
            if (!EsSinIva(ivaCabecera) || lineas == null)
            {
                return normalizadas;
            }
            foreach (LinPedidoVta linea in lineas.Where(l => EsViva(l) && LlevaIva(l)))
            {
                PonerSinIva(linea);
                normalizadas.Add(linea);
            }
            return normalizadas;
        }

        /// <summary>
        /// Con IVA en la cabecera, ¿hay líneas vivas de producto con el MISMO código de IVA y porcentajes distintos (unas a 0 y
        /// otras no)? No se ha visto en producción: solo se avisa (ELMAH), no se toca nada. Null si no hay mezcla.
        /// </summary>
        public static string MotivoIvaMixto(string ivaCabecera, IEnumerable<LinPedidoVta> lineas)
        {
            if (EsSinIva(ivaCabecera) || lineas == null)
            {
                return null;
            }
            List<string> mezclas = lineas
                .Where(l => EsViva(l) && l.TipoLinea == Constantes.TiposLineaVenta.PRODUCTO && !string.IsNullOrWhiteSpace(l.IVA))
                .GroupBy(l => l.IVA.Trim().ToUpperInvariant())
                .Where(g => g.Any(l => l.PorcentajeIVA == 0) && g.Any(l => l.PorcentajeIVA != 0))
                .Select(g => $"{g.Key}: a 0 % las líneas {string.Join(", ", g.Where(l => l.PorcentajeIVA == 0).Select(l => DescribirLinea(l)))}" +
                    $" y con IVA las {string.Join(", ", g.Where(l => l.PorcentajeIVA != 0).Select(l => DescribirLinea(l) + " (" + l.PorcentajeIVA + " %)"))}")
                .ToList();
            return mezclas.Any() ? string.Join("; ", mezclas) : null;
        }

        public static string MensajeNormalizadas(string empresa, int numero, string operacion, string usuario, IEnumerable<LinPedidoVta> normalizadas) =>
            $"[Pedido sin IVA: líneas con IVA normalizadas] {operacion} del pedido {empresa?.Trim()}/{numero} (usuario {usuario}): la cabecera " +
            $"no tiene IVA y estas líneas vivas lo llevaban; se guardan a 0 %: {string.Join(", ", normalizadas.Select(l => DescribirLinea(l)))}.";

        public static string MensajeMixto(string empresa, int numero, string operacion, string usuario, string ivaCabecera, string motivo) =>
            $"[Pedido con IVA mixto] {operacion} del pedido {empresa?.Trim()}/{numero} (usuario {usuario}, cabecera {ivaCabecera?.Trim()}): {motivo}. " +
            "No se ha cambiado nada.";

        private static string DescribirLinea(LinPedidoVta l) =>
            (l.Nº_Orden == 0 ? "nueva" : l.Nº_Orden.ToString()) + " [" + l.Producto?.Trim() + "]";
    }
}
