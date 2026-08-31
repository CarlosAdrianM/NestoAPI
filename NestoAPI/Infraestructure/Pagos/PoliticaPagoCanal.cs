using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.Pagos
{
    /// <summary>
    /// NestoAPI#436: la política de cobro por canal, en un solo sitio.
    ///
    /// <para>Para la app móvil (canal <see cref="Constantes.FormasVenta.APP"/>) es más restrictiva
    /// que la general de <c>GET api/PlazosPago/CondicionesPago</c>:</para>
    /// <list type="number">
    /// <item>Por defecto, <b>tarjeta al contado</b> (TAR + PRE). Es lo que interesa al negocio y
    /// además deja el token de tarjeta guardado para cobros sucesivos (NestoAPI#178).</item>
    /// <item>El <b>crédito es opcional y solo si su ficha lo permite</b>: se ofrecen los plazos y
    /// formas de pago que el cliente tenga en CondPagoClientes, pero nunca por defecto. Los
    /// plazos "de cortesía" que el selector general concede a los clientes buenos (hasta 6
    /// plazos por antigüedad) NO se ofrecen en la app: ahí no hay un vendedor decidiendo.</item>
    /// <item>Con <b>impagados o deuda vencida, solo TAR</b>. La política general deja las tres
    /// formas de pago seguras (EFC, TRN, TAR) y recomienda efectivo, pero en una app no tiene
    /// sentido ofrecer "pago en efectivo" y una transferencia no garantiza el cobro antes de
    /// servir.</item>
    /// </list>
    ///
    /// <para>Todos los métodos son puros: reciben lo que ya han calculado el selector de plazos y
    /// la ficha del cliente, y devuelven qué se puede ofrecer. Así la política se puede probar sin
    /// base de datos y la comparten el selector (PlazosPagoController) y el endpoint de pedidos de
    /// cliente (PedidosClienteController), que es lo que evita que se separen con el tiempo.</para>
    /// </summary>
    public static class PoliticaPagoCanal
    {
        /// <summary>Condiciones de pago que el cliente tiene en su ficha (CondPagoClientes).</summary>
        public class CondicionesFicha
        {
            public CondicionesFicha()
            {
                FormasPago = new List<string>();
                PlazosPago = new List<string>();
            }
            public ICollection<string> FormasPago { get; set; }
            public ICollection<string> PlazosPago { get; set; }
        }

        public static bool EsApp(string canal)
        {
            return !string.IsNullOrWhiteSpace(canal) &&
                string.Equals(canal.Trim(), Constantes.FormasVenta.APP, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Aplica la política de la app sobre las condiciones de pago generales. Devuelve una
        /// respuesta nueva; no toca la que recibe.
        /// </summary>
        public static CondicionesPagoResponse AplicarPoliticaApp(CondicionesPagoResponse condiciones, CondicionesFicha ficha)
        {
            if (condiciones == null)
            {
                return null;
            }
            ficha = ficha ?? new CondicionesFicha();
            bool tieneDeuda = TieneDeuda(condiciones.InfoDeuda);

            return new CondicionesPagoResponse
            {
                FormasPago = FiltrarFormasPagoApp(condiciones.FormasPago, tieneDeuda, ficha.FormasPago),
                PlazosPago = FiltrarPlazosPagoApp(condiciones.PlazosPago, tieneDeuda, ficha.PlazosPago),
                InfoDeuda = condiciones.InfoDeuda,
                // En la app siempre se recomienda tarjeta al contado, tenga deuda o no. El
                // efectivo que recomienda la política general no existe como opción aquí.
                FormaPagoRecomendada = Constantes.FormasPago.TARJETA,
                PlazoPagoRecomendado = Constantes.PlazosPago.PREPAGO
            };
        }

        public static bool TieneDeuda(InfoDeudaClienteDTO infoDeuda)
        {
            return infoDeuda != null && (infoDeuda.TieneImpagados || infoDeuda.TieneDeudaVencida);
        }

        internal static List<FormaPagoDTO> FiltrarFormasPagoApp(List<FormaPagoDTO> formasPago, bool tieneDeuda, ICollection<string> formasPagoFicha)
        {
            if (formasPago == null)
            {
                return new List<FormaPagoDTO>();
            }
            // Con deuda, solo tarjeta: es la única que garantiza el cobro en el momento del pedido.
            if (tieneDeuda)
            {
                return formasPago.Where(f => EsTarjeta(f.formaPago)).ToList();
            }
            HashSet<string> deLaFicha = Normalizar(formasPagoFicha);
            return formasPago
                .Where(f => EsTarjeta(f.formaPago) || deLaFicha.Contains(Normalizar(f.formaPago)))
                .ToList();
        }

        internal static List<PlazoPagoDTO> FiltrarPlazosPagoApp(List<PlazoPagoDTO> plazosPago, bool tieneDeuda, ICollection<string> plazosPagoFicha)
        {
            if (plazosPago == null)
            {
                return new List<PlazoPagoDTO>();
            }
            // Con deuda, nada de financiación: solo lo que se cobra en el momento del pedido.
            if (tieneDeuda)
            {
                return plazosPago.Where(EsAlContado).ToList();
            }
            HashSet<string> deLaFicha = Normalizar(plazosPagoFicha);
            return plazosPago
                .Where(p => EsAlContado(p) || deLaFicha.Contains(Normalizar(p.plazoPago)))
                .ToList();
        }

        /// <summary>
        /// Forma de pago con la que se crea el pedido: la que pide el cliente solo si la política
        /// se la permite; si no, la recomendada. Nunca se cree lo que llega en la petición.
        /// </summary>
        public static string ResolverFormaPago(CondicionesPagoResponse condiciones, string formaPagoSolicitada)
        {
            string recomendada = condiciones?.FormaPagoRecomendada ?? Constantes.FormasPago.TARJETA;
            if (string.IsNullOrWhiteSpace(formaPagoSolicitada) || condiciones?.FormasPago == null)
            {
                return recomendada;
            }
            FormaPagoDTO permitida = condiciones.FormasPago
                .FirstOrDefault(f => Normalizar(f.formaPago) == Normalizar(formaPagoSolicitada));
            return permitida != null ? permitida.formaPago.Trim() : recomendada;
        }

        /// <summary>
        /// Plazos de pago con los que se crea el pedido, con el mismo criterio que
        /// <see cref="ResolverFormaPago"/>.
        /// </summary>
        public static string ResolverPlazosPago(CondicionesPagoResponse condiciones, string plazosPagoSolicitados)
        {
            string recomendado = condiciones?.PlazoPagoRecomendado ?? Constantes.PlazosPago.PREPAGO;
            if (string.IsNullOrWhiteSpace(plazosPagoSolicitados) || condiciones?.PlazosPago == null)
            {
                return recomendado;
            }
            PlazoPagoDTO permitido = condiciones.PlazosPago
                .FirstOrDefault(p => Normalizar(p.plazoPago) == Normalizar(plazosPagoSolicitados));
            return permitido != null ? permitido.plazoPago.Trim() : recomendado;
        }

        /// <summary>
        /// ¿El pedido se cobra en el momento? Es lo que decide si la app tiene que llevar al
        /// cliente a la pasarela nada más crearlo.
        /// </summary>
        public static bool SeCobraEnElMomento(string formaPago, string plazosPago)
        {
            return EsTarjeta(formaPago) ||
                Normalizar(plazosPago) == Normalizar(Constantes.PlazosPago.PREPAGO);
        }

        private static bool EsTarjeta(string formaPago)
        {
            return Normalizar(formaPago) == Normalizar(Constantes.FormasPago.TARJETA);
        }

        private static bool EsAlContado(PlazoPagoDTO plazo)
        {
            return plazo != null && plazo.numeroPlazos == 1 && plazo.diasPrimerPlazo == 0 && plazo.mesesPrimerPlazo == 0;
        }

        // Los códigos de la base de datos vienen rellenos de espacios por la izquierda o la derecha
        // según de dónde salgan (son char de longitud fija), así que se comparan recortados.
        private static string Normalizar(string codigo)
        {
            return codigo == null ? string.Empty : codigo.Trim().ToUpperInvariant();
        }

        private static HashSet<string> Normalizar(ICollection<string> codigos)
        {
            return codigos == null
                ? new HashSet<string>()
                : new HashSet<string>(codigos.Where(c => !string.IsNullOrWhiteSpace(c)).Select(Normalizar));
        }
    }
}
