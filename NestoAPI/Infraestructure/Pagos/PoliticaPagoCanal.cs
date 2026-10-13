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

            /// <summary>
            /// NestoAPI#436: el CCC que lleva la ficha sobre la que se va a crear el pedido. Sin él
            /// no se puede ofrecer una forma de pago domiciliada, porque el pedido saldría
            /// domiciliado a ninguna cuenta. El selector general ya quita esas formas cuando el
            /// cliente no tiene NINGÚN CCC dado de alta, pero eso no es lo mismo: puede tener
            /// cuentas y no tener ninguna asignada en la ficha con la que se pide.
            /// </summary>
            public string Ccc { get; set; }
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
                FormasPago = FiltrarFormasPagoApp(condiciones.FormasPago, tieneDeuda, ficha.FormasPago, ficha.Ccc),
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

        internal static List<FormaPagoDTO> FiltrarFormasPagoApp(
            List<FormaPagoDTO> formasPago, bool tieneDeuda, ICollection<string> formasPagoFicha, string cccDeLaFicha)
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
                .Where(f => !EsEfectivo(f.formaPago))
                .Where(f => PuedeDomiciliarse(f, cccDeLaFicha))
                .ToList();
        }

        internal static List<PlazoPagoDTO> FiltrarPlazosPagoApp(List<PlazoPagoDTO> plazosPago, bool tieneDeuda, ICollection<string> plazosPagoFicha)
        {
            if (plazosPago == null)
            {
                return new List<PlazoPagoDTO>();
            }
            HashSet<string> deLaFicha = Normalizar(plazosPagoFicha);
            // NestoAPI#593 (TNV): los de pronto pago (PP, PP2, PP5) también son al contado, pero llevan un descuento:
            // solo para quien lo tiene en su ficha. Antes valían para cualquiera que los pidiera en la petición.
            List<PlazoPagoDTO> sinDescuentoAjeno = plazosPago
                .Where(p => p.descuentoPP <= 0 || deLaFicha.Contains(Normalizar(p.plazoPago)))
                .ToList();
            // Con deuda, nada de financiación: solo lo que se cobra en el momento del pedido.
            if (tieneDeuda)
            {
                return sinDescuentoAjeno.Where(EsAlContado).ToList();
            }
            return sinDescuentoAjeno
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
        /// TNV#55: plazos con los que se crea el pedido de la app cuando el cliente paga con una forma de
        /// su ficha («Recibo bancario»). La app manda como plazos el <c>PlazoPagoRecomendado</c>, que en
        /// el canal APP es siempre PRE (el de la tarjeta), así que el pedido salía RCB/PRE: sin su pronto
        /// pago y con <c>RequierePago</c>, es decir, a la pasarela aunque hubiera elegido el recibo.
        ///
        /// <para>Ahora, si la forma no es tarjeta y los plazos se quedan en PRE, se usan <b>sus plazos
        /// habituales</b> para esa forma de pago (los de su ficha, como si hubiera llamado a su
        /// vendedor): <see cref="PlazosHabituales"/>. Solo si están entre los que la política del canal
        /// le deja (<paramref name="condiciones"/>): nada de esto amplía quién paga con recibo ni con
        /// qué plazos. Si para ese importe su ficha no tiene ninguno (el umbral de la única fila es
        /// mayor que el pedido), al contado domiciliado (CONTADO), sin financiación ni descuento.</para>
        /// </summary>
        public static string ResolverPlazosPago(CondicionesPagoResponse condiciones, string formaPago,
            string plazosPagoSolicitados, IEnumerable<CondPagoCliente> ficha, string contacto, decimal totalPedido)
        {
            string plazos = ResolverPlazosPago(condiciones, plazosPagoSolicitados);
            if (EsTarjeta(formaPago) || string.IsNullOrWhiteSpace(formaPago)
                || Normalizar(plazos) != Normalizar(Constantes.PlazosPago.PREPAGO))
            {
                return plazos;
            }
            string habituales = PlazosHabituales(ficha, formaPago, contacto, totalPedido);
            if (EstaPermitido(condiciones, habituales))
            {
                return habituales.Trim();
            }
            return EstaPermitido(condiciones, Constantes.PlazosPago.CONTADO)
                ? Constantes.PlazosPago.CONTADO
                : plazos;
        }

        /// <summary>
        /// TNV#55: los plazos habituales del cliente para una forma de pago y un importe, con la regla con
        /// la que nace un pedido en Nesto (<c>PlantillaVentas/DireccionesEntrega</c>,
        /// <c>CambioClientePedido.ResolverCondicionesPago</c>): de las filas de CondPagoClientes de esa
        /// forma, la de mayor <c>ImporteMínimo</c> que no supere el total (el ImporteMínimo es un UMBRAL).
        /// Las filas son por contacto: primero las de la dirección del pedido; si esa dirección no tiene
        /// ninguna de esa forma, las del resto de su ficha. Null si ninguna vale para ese importe.
        /// </summary>
        public static string PlazosHabituales(IEnumerable<CondPagoCliente> ficha, string formaPago, string contacto, decimal totalPedido)
        {
            List<CondPagoCliente> deLaForma = (ficha ?? Enumerable.Empty<CondPagoCliente>())
                .Where(c => c != null && !string.IsNullOrWhiteSpace(c.PlazosPago)
                    && Normalizar(c.FormaPago) == Normalizar(formaPago))
                .ToList();
            List<CondPagoCliente> delContacto = deLaForma
                .Where(c => Normalizar(c.Contacto) == Normalizar(contacto))
                .ToList();
            IEnumerable<CondPagoCliente> candidatas = delContacto.Any() ? delContacto : deLaForma;
            return candidatas
                .OrderByDescending(c => c.ImporteMínimo)
                .FirstOrDefault(c => c.ImporteMínimo <= totalPedido)
                ?.PlazosPago?.Trim();
        }

        /// <summary>
        /// TNV#55: el importe con el que se eligen los plazos habituales, el mismo que manda la plantilla de Nesto
        /// (<c>PlantillaVentaViewModel.totalPedido</c>): la base imponible sin portes, con el 21 % si el cliente
        /// lleva IVA. El umbral de CondPagoClientes se puso pensando en ese número.
        /// </summary>
        public static decimal ImporteParaPlazos(decimal baseImponible, string ivaCliente)
        {
            return string.IsNullOrWhiteSpace(ivaCliente) ? baseImponible : baseImponible * 1.21M;
        }

        private static bool EstaPermitido(CondicionesPagoResponse condiciones, string plazos)
        {
            return !string.IsNullOrWhiteSpace(plazos) && condiciones?.PlazosPago != null
                && condiciones.PlazosPago.Any(p => Normalizar(p.plazoPago) == Normalizar(plazos));
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

        /// <summary>
        /// NestoAPI#436: el efectivo NO se ofrece en la app, ni con deuda ni sin ella. El argumento
        /// que lo excluía en el caso de deuda vale igual sin deuda: en una app no hay a quién darle
        /// el efectivo, y un cliente que lo eligiera no sabría qué acaba de pedir.
        ///
        /// <para>OJO con lo que esto significa de verdad: en esta casa EFC sin CCC es el CONTRA
        /// REEMBOLSO (lo mira <c>GestorPortes.EsContraReembolso</c>, con su comisión). Así que esto
        /// no es solo "quitar el efectivo": es no ofrecer contra reembolso en la app. Si el negocio
        /// lo quiere, se quita este filtro y la app tiene que pintarlo como "contra reembolso" con
        /// su comisión a la vista, no como "efectivo".</para>
        /// </summary>
        private static bool EsEfectivo(string formaPago)
        {
            return Normalizar(formaPago) == Normalizar(Constantes.FormasPago.EFECTIVO);
        }

        /// <summary>
        /// NestoAPI#436: una forma de pago domiciliada necesita una cuenta a la que domiciliar. El
        /// pedido de la app se crea con el CCC de la ficha, así que si ahí no hay ninguno, saldría
        /// domiciliado a ninguna cuenta.
        /// </summary>
        private static bool PuedeDomiciliarse(FormaPagoDTO formaPago, string cccDeLaFicha)
        {
            return !formaPago.cccObligatorio || !string.IsNullOrWhiteSpace(cccDeLaFicha);
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
