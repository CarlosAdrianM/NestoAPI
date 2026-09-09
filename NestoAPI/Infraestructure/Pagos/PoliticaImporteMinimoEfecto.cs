using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.Pagos
{
    /// <summary>
    /// NestoAPI#458: la regla de los 150 € por efecto (<see cref="Constantes.PlazosPago.IMPORTE_MINIMO_EFECTO"/>),
    /// en un solo sitio. Antes vivía inline en <c>GET api/PlazosPago?totalPedido</c> y no la aplicaba
    /// <c>GET api/PlazosPago/CondicionesPago</c>, así que la tienda (nestopago) y la app veían
    /// «30/60/90» para un pedido de 200 € y Nesto solo se enteraba por el correo de «financiación a
    /// revisar». Es el mismo tipo de divergencia que costó el 100/150 de NestoAPI#396.
    ///
    /// <para>Reglas puras: un plazo se ofrece si es al contado, si cada efecto llega al mínimo, o si
    /// el cliente lo tiene autorizado en su ficha (CondPagoClientes) PARA ESE IMPORTE: el
    /// <c>ImporteMínimo</c> de la ficha es un umbral, la autorización vale a partir de él.</para>
    /// </summary>
    public static class PoliticaImporteMinimoEfecto
    {
        /// <summary>Una fila de CondPagoClientes, reducida a lo que necesita la regla.</summary>
        public class CondicionFicha
        {
            public string PlazosPago { get; set; }
            public decimal ImporteMinimo { get; set; }
        }

        /// <summary>
        /// Las condiciones de la ficha que valen para este importe. Con total 0 o negativo (un
        /// abono, o el selector sin importe) solo valen las de importe mínimo 0.
        /// </summary>
        public static List<CondicionFicha> CondicionesQueAplican(IEnumerable<CondicionFicha> ficha, decimal totalPedido)
        {
            return (ficha ?? Enumerable.Empty<CondicionFicha>())
                .Where(c => c != null && (c.ImporteMinimo <= totalPedido || (totalPedido <= 0 && c.ImporteMinimo == 0)))
                .ToList();
        }

        /// <summary>
        /// Importe de cada efecto. Con entrada (primer plazo a 0 días y 0 meses) la entrada no
        /// cuenta como efecto: el pedido se reparte entre los <c>numeroPlazos - 1</c> restantes.
        /// </summary>
        public static decimal ImportePorEfecto(PlazoPagoDTO plazo, decimal totalPedido)
        {
            if (plazo == null || plazo.numeroPlazos <= 1)
            {
                return totalPedido;
            }
            bool tieneEntrada = plazo.diasPrimerPlazo + plazo.mesesPrimerPlazo == 0;
            int efectos = tieneEntrada ? plazo.numeroPlazos - 1 : plazo.numeroPlazos;
            return efectos <= 0 ? totalPedido : totalPedido / efectos;
        }

        public static bool EsAlContado(PlazoPagoDTO plazo)
        {
            return plazo != null && plazo.numeroPlazos <= 1;
        }

        public static bool CumpleImporteMinimo(PlazoPagoDTO plazo, decimal totalPedido)
        {
            return EsAlContado(plazo) || ImportePorEfecto(plazo, totalPedido) >= Constantes.PlazosPago.IMPORTE_MINIMO_EFECTO;
        }

        /// <summary>
        /// Los plazos que se pueden ofrecer para un pedido de <paramref name="totalPedido"/>.
        /// Nunca lanza: si algo no cuadra, el plazo simplemente no pasa.
        /// </summary>
        public static List<PlazoPagoDTO> Filtrar(IEnumerable<PlazoPagoDTO> plazosPago, decimal totalPedido, IEnumerable<CondicionFicha> ficha)
        {
            if (plazosPago == null)
            {
                return new List<PlazoPagoDTO>();
            }
            HashSet<string> autorizados = new HashSet<string>(
                CondicionesQueAplican(ficha, totalPedido)
                    .Select(c => Normalizar(c.PlazosPago))
                    .Where(p => p.Length > 0));

            return plazosPago
                .Where(p => p != null)
                .Where(p => CumpleImporteMinimo(p, totalPedido) || autorizados.Contains(Normalizar(p.plazoPago)))
                .ToList();
        }

        /// <summary>
        /// Lo que hace <c>GET CondicionesPago</c> cuando llega <c>totalPedido</c>: se aplica DESPUÉS
        /// de la política del canal, sobre los plazos que esa política ya dejó, para que el resultado
        /// siga siendo «contado más lo de la ficha» y además cumpla el importe. Sin importe no se
        /// toca nada (compatibilidad con quien no lo manda).
        /// </summary>
        public static CondicionesPagoResponse AplicarSiHayImporte(CondicionesPagoResponse condiciones, decimal? totalPedido, IEnumerable<CondicionFicha> ficha)
        {
            if (condiciones == null || !totalPedido.HasValue)
            {
                return condiciones;
            }
            condiciones.PlazosPago = Filtrar(condiciones.PlazosPago, totalPedido.Value, ficha);
            return condiciones;
        }

        private static string Normalizar(string codigo)
        {
            return codigo == null ? string.Empty : codigo.Trim().ToUpperInvariant();
        }
    }
}
