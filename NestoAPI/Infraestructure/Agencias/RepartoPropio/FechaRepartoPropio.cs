using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using NestoAPI.Models.RecursosHumanos;

namespace NestoAPI.Infraestructure.Agencias.RepartoPropio
{
    /// <summary>
    /// NestoAPI#831 (Carlos 06/11/26): el día de reparto de un envío del reparto propio (agencia 14) va en
    /// <c>EnviosAgencia.FechaEntrega</c>; <c>Fecha</c> sigue siendo el día en que se hizo la etiqueta. Así el mozo cierra el bulto
    /// y saca la etiqueta aunque el pedido se reparta otro día (un aplazamiento, #722), y la furgoneta solo lo carga ese día.
    /// <para>LA REGLA (<see cref="Calcular"/>): si el picking del pedido es de hoy o de antes (o no hay), hoy, como siempre; si es
    /// de un día futuro, ese día, pasado por el día de reparto de nuestra ruta (<see cref="DiasRepartoCodigosPostales.DiaRutaInterna(string, string, string, string, string)"/>)
    /// y por los días en que no se reparte por nuestra ruta (<see cref="EsDiaSinReparto"/>: festivos de Algete y, NestoAPI#841, días
    /// sin reparto propio).</para>
    /// </summary>
    public static class FechaRepartoPropio
    {
        /// <summary>Como mucho se salta este número de días seguidos de festivos (nunca un bucle sin fin).</summary>
        internal const int MAXIMO_DIAS_SALTO = 30;

        /// <summary>
        /// ¿Ese día no sale la furgoneta? Los festivos de Algete (de donde sale) y, NestoAPI#841, los días sin reparto propio
        /// (<see cref="TipoFestivo.RepartoPropio"/>): <see cref="GestorFestivos.EsFestivoRepartoPropio(DateTime)"/>.
        /// </summary>
        public static bool EsDiaSinReparto(DateTime dia) => GestorFestivos.EsFestivoRepartoPropio(dia);

        /// <summary>LA REGLA (ver la clase).</summary>
        /// <param name="fechaEntregaPicking">La MIN fecha de entrega de las líneas con picking del pedido (null si no hay).</param>
        /// <param name="diaRuta">El día de reparto de nuestra ruta para ese cliente y código postal, o null si no tiene.</param>
        public static DateTime Calcular(DateTime? fechaEntregaPicking, DateTime hoy, DayOfWeek? diaRuta, Func<DateTime, bool> esFestivo)
        {
            DateTime dia = hoy.Date;
            if (!fechaEntregaPicking.HasValue || fechaEntregaPicking.Value.Date <= dia)
            {
                return dia;
            }
            DateTime desde = fechaEntregaPicking.Value.Date;
            Func<DateTime, bool> festivo = esFestivo ?? (d => false);
            if (diaRuta.HasValue)
            {
                return CalendarioRutasDia.ProximoDiaReparto(diaRuta.Value, desde, festivo);
            }
            DateTime resultado = desde;
            for (int i = 0; i < MAXIMO_DIAS_SALTO && festivo(resultado); i++)
            {
                resultado = resultado.AddDays(1);
            }
            return resultado;
        }

        /// <summary>
        /// LA REGLA con lo que se sabe del pedido (su ruta, cliente, contacto y el código postal de entrega). Nunca lanza por el
        /// día de reparto: sin tablas de días, solo cuentan los festivos.
        /// </summary>
        public static DateTime Calcular(DateTime? fechaEntregaPicking, DateTime hoy, string ruta, string codigoPostal, string empresa,
            string cliente, string contacto, Func<DateTime, bool> esFestivo)
        {
            DayOfWeek? dia = DiasRepartoCodigosPostales.DiaRutaInterna(ruta?.Trim(), codigoPostal?.Trim(), empresa?.Trim(), cliente?.Trim(), contacto?.Trim());
            return Calcular(fechaEntregaPicking, hoy, dia, esFestivo);
        }

        /// <summary>El día de reparto de un envío del reparto propio: su FechaEntrega (o, sin ella, su Fecha), sin hora.</summary>
        public static DateTime DeEnvio(EnviosAgencia envio)
            => envio == null ? DateTime.MinValue : (envio.FechaEntrega ?? envio.Fecha).Date;

        /// <summary>¿Es un envío del reparto propio que se reparte DESPUÉS de <paramref name="hoy"/>?</summary>
        public static bool EsDeOtroDia(EnviosAgencia envio, DateTime hoy)
            => envio != null && envio.Agencia == Constantes.Agencias.AGENCIA_REPARTO_PROPIO && DeEnvio(envio) > hoy.Date;

        /// <summary>«Este bulto es del reparto del 09/11: no se carga hoy.»</summary>
        public static string MensajeNoSeCargaHoy(EnviosAgencia envio)
            => $"Este bulto es del reparto del {DeEnvio(envio).ToString("dd/MM", CultureInfo.InvariantCulture)}: no se carga hoy.";

        /// <summary>La MIN fecha de entrega de las líneas en picking (Estado 1, Picking &gt; 0) del pedido, o null.</summary>
        public static async Task<DateTime?> FechaEntregaPickingAsync(NVEntities db, string empresa, int pedido)
        {
            string empresaLimpia = string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
            List<DateTime> fechas = await db.LinPedidoVtas.AsNoTracking()
                .Where(l => l.Empresa == empresaLimpia && l.Número == pedido && l.Estado == Constantes.EstadosLineaVenta.EN_CURSO && l.Picking > 0)
                .Select(l => l.Fecha_Entrega)
                .ToListAsync().ConfigureAwait(false);
            return fechas.Count == 0 ? (DateTime?)null : fechas.Min();
        }

        /// <summary>
        /// LA REGLA para el envío <paramref name="envio"/> (agencia 14) con lo que hay en la BD: el picking de su pedido y la ruta,
        /// cliente y contacto de la cabecera, con el código postal del envío. Sin pedido, hoy.
        /// </summary>
        public static async Task<DateTime> DelEnvioAsync(NVEntities db, EnviosAgencia envio, DateTime hoy, Func<DateTime, bool> esFestivo)
        {
            if (envio?.Pedido == null || envio.Pedido.Value <= 0)
            {
                return hoy.Date;
            }
            int pedido = envio.Pedido.Value;
            // El pedido es de la empresa del envío o, en la espejo, de la 1: se busca en las dos, primero en la suya.
            var empresas = new List<string> { envio.Empresa?.Trim(), Constantes.Empresas.EMPRESA_POR_DEFECTO }
                .Where(e => !string.IsNullOrWhiteSpace(e)).Distinct().ToList();
            foreach (string empresa in empresas)
            {
                var cabecera = (await db.CabPedidoVtas.AsNoTracking()
                    .Where(c => c.Empresa == empresa && c.Número == pedido)
                    .Select(c => new { c.Ruta, c.Nº_Cliente, c.Contacto })
                    .ToListAsync().ConfigureAwait(false)).FirstOrDefault();
                if (cabecera == null)
                {
                    continue;
                }
                DateTime? picking = await FechaEntregaPickingAsync(db, empresa, pedido).ConfigureAwait(false);
                return Calcular(picking, hoy, cabecera.Ruta, envio.CodPostal, empresa, cabecera.Nº_Cliente, cabecera.Contacto, esFestivo);
            }
            return hoy.Date;
        }
    }
}
