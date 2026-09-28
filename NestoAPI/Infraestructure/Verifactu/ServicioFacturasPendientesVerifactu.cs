using NestoAPI.Models;
using NestoAPI.Models.Facturas;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Verifactu
{
    public interface IServicioFacturasPendientesVerifactu
    {
        /// <summary>Las facturas que Verifactu todavía no da por buenas, de la más antigua a la más reciente.</summary>
        Task<List<FacturaPendienteVerifactuDTO>> Listar();

        /// <summary>Vuelve a enviar la factura a Verifactu (alta o subsanación, según esté). Null si no existe.</summary>
        Task<ResultadoReintentoVerifactuDTO> Reintentar(string empresa, string numeroFactura);
    }

    /// <summary>
    /// NestoAPI#522: lo que ve y hace administración en la ventana de facturas pendientes de Verifactu de Nesto.
    /// Salen las facturas de las series que tramitan, desde el arranque de la declaración (la misma guarda que
    /// el job: nunca el histórico), que:
    /// - no tienen registro (sin UUID): pendientes de enviar, rechazadas por Verifacti antes de llegar a la AEAT,
    ///   pendientes por incidencia técnica o sin datos fiscales; o
    /// - tienen registro pero la AEAT lo ha dado por incorrecto o rechazado (el job guarda el motivo).
    /// El motivo es VerifactuUltimoError (el último error del envío o el veredicto de la AEAT).
    /// «Reintentar» hace lo mismo que el job (alta, o subsanación si es antigua) y, para las incorrectas en la
    /// AEAT, la subsanación con rechazo_previo = X.
    /// </summary>
    public class ServicioFacturasPendientesVerifactu : IServicioFacturasPendientesVerifactu
    {
        internal const string SITUACION_PENDIENTE = "Pendiente de enviar";
        internal const string SITUACION_RECHAZADA_VERIFACTI = "Rechazada al enviarla";
        internal const string SITUACION_INCIDENCIA = "Pendiente por incidencia técnica";
        internal const string SITUACION_INCIDENCIA_ANTIGUA = "Incidencia técnica de hace más de un día";
        internal const string SITUACION_SIN_DATOS_FISCALES = "Sin datos fiscales";
        internal const string SITUACION_INCORRECTA_AEAT = "Incorrecta en la AEAT";

        private const int MAXIMO_FACTURAS = 500;

        private readonly NVEntities db;
        private readonly Func<CabFacturaVta, Task<VerifactuResponse>> reenviar;
        private readonly Func<CabFacturaVta, Task<VerifactuResponse>> subsanar;
        private readonly Func<DateTime> hoy;

        public ServicioFacturasPendientesVerifactu() : this(null)
        {
        }

        internal ServicioFacturasPendientesVerifactu(NVEntities db,
            Func<CabFacturaVta, Task<VerifactuResponse>> reenviar = null,
            Func<CabFacturaVta, Task<VerifactuResponse>> subsanar = null,
            Func<DateTime> hoy = null)
        {
            this.db = db ?? new NVEntities();
            this.reenviar = reenviar ?? ReenviarConServicioFacturas;
            this.subsanar = subsanar ?? SubsanarConServicioFacturas;
            this.hoy = hoy ?? (() => DateTime.Today);
        }

        public async Task<List<FacturaPendienteVerifactuDTO>> Listar()
        {
            List<string> series = RegistroSeriesVerifactu.CodigosQueTramitan;
            DateTime fechaInicio = VerifactuJobsService.FechaInicioDeclaracion;
            List<CabFacturaVta> facturas = await db.CabsFacturasVtas
                .Where(f => f.Fecha >= fechaInicio && series.Contains(f.Serie)
                    && (f.VerifactuUUID == null || f.VerifactuUUID == ""
                        || (f.VerifactuEstado != null
                            && (f.VerifactuEstado.Contains("Incorrecto") || f.VerifactuEstado.Contains("Rechaz")))))
                .OrderBy(f => f.Fecha).ThenBy(f => f.Número)
                .Take(MAXIMO_FACTURAS)
                .ToListAsync().ConfigureAwait(false);

            // Las que no tienen datos fiscales persistidos (camino viejo #348) no traen nombre: se busca en la ficha.
            List<string> clientesSinNombre = facturas
                .Where(f => string.IsNullOrWhiteSpace(f.NombreFiscal))
                .Select(f => f.Nº_Cliente)
                .Distinct()
                .ToList();
            List<Cliente> fichas = clientesSinNombre.Any()
                ? await db.Clientes.Where(c => clientesSinNombre.Contains(c.Nº_Cliente)).ToListAsync().ConfigureAwait(false)
                : new List<Cliente>();

            return facturas
                .Where(f => !EstaRegistrada(f)) // la consulta usa LIKE; aquí se aplica la misma regla que el job
                .Select(f => Mapear(f, fichas))
                .ToList();
        }

        public async Task<ResultadoReintentoVerifactuDTO> Reintentar(string empresa, string numeroFactura)
        {
            string numero = numeroFactura?.Trim();
            string codigoEmpresa = empresa?.Trim();
            CabFacturaVta factura = await db.CabsFacturasVtas
                .FirstOrDefaultAsync(f => f.Empresa == codigoEmpresa && f.Número == numero).ConfigureAwait(false);
            if (factura == null)
            {
                return null;
            }
            if (!RegistroSeriesVerifactu.TramitaVerifactu(factura.Serie))
            {
                return new ResultadoReintentoVerifactuDTO
                {
                    Exitoso = false,
                    Mensaje = $"La factura {numero} es de la serie {factura.Serie?.Trim()}, que no se declara a Verifactu."
                };
            }
            if (EstaRegistrada(factura))
            {
                return new ResultadoReintentoVerifactuDTO
                {
                    Exitoso = true,
                    Mensaje = $"La factura {numero} ya está registrada en Verifactu (estado {factura.VerifactuEstado?.Trim()}). No hay nada que reenviar."
                };
            }

            FacturaPendienteVerifactuDTO antes = Mapear(factura, null);
            if (!antes.PuedeReintentar)
            {
                return new ResultadoReintentoVerifactuDTO { Exitoso = false, Mensaje = antes.QueHacer, Factura = antes };
            }

            bool esIncorrectaEnLaAeat = TieneRegistro(factura);
            VerifactuResponse respuesta = esIncorrectaEnLaAeat
                ? await subsanar(factura).ConfigureAwait(false)
                : await reenviar(factura).ConfigureAwait(false);

            var resultado = new ResultadoReintentoVerifactuDTO();
            if (respuesta != null && respuesta.Exitoso)
            {
                resultado.Exitoso = true;
                resultado.Mensaje = $"La factura {numero} se ha enviado a Verifactu" +
                    (esIncorrectaEnLaAeat ? " como subsanación" : string.Empty) +
                    ". La AEAT la confirma en unos minutos; si la volviera a rechazar, saldrá otra vez en esta lista con el motivo.";
            }
            else if (respuesta != null)
            {
                resultado.Exitoso = false;
                resultado.Mensaje = $"Verifactu no ha aceptado la factura {numero}: " +
                    $"{respuesta.CodigoError} {respuesta.MensajeError}".Trim();
            }
            else
            {
                resultado.Exitoso = false;
                resultado.Mensaje = $"No se ha podido enviar la factura {numero}. " +
                    (string.IsNullOrWhiteSpace(factura.VerifactuUltimoError)
                        ? "El motivo ha quedado registrado en ELMAH."
                        : "Motivo: " + factura.VerifactuUltimoError.Trim());
            }
            // Tal y como queda (el envío actualiza la misma entidad): null si ya no está pendiente.
            resultado.Factura = EstaRegistrada(factura) ? null : Mapear(factura, null);
            return resultado;
        }

        /// <summary>Con registro en Verifactu (UUID).</summary>
        private static bool TieneRegistro(CabFacturaVta factura)
        {
            return !string.IsNullOrWhiteSpace(factura.VerifactuUUID);
        }

        /// <summary>Registrada y sin rechazo de la AEAT: no tiene nada que hacer aquí.</summary>
        internal static bool EstaRegistrada(CabFacturaVta factura)
        {
            return TieneRegistro(factura) && !VerifactuJobsService.EsEstadoDeRechazo(factura.VerifactuEstado);
        }

        internal FacturaPendienteVerifactuDTO Mapear(CabFacturaVta factura, List<Cliente> fichas)
        {
            string nombre = factura.NombreFiscal?.Trim();
            if (string.IsNullOrWhiteSpace(nombre) && fichas != null)
            {
                nombre = fichas.FirstOrDefault(c => c.Empresa?.Trim() == factura.Empresa?.Trim()
                        && c.Nº_Cliente?.Trim() == factura.Nº_Cliente?.Trim()
                        && c.Contacto?.Trim() == factura.Contacto?.Trim())?.Nombre?.Trim();
            }
            string situacion = Situacion(factura, hoy());
            return new FacturaPendienteVerifactuDTO
            {
                Empresa = factura.Empresa?.Trim(),
                Numero = factura.Número?.Trim(),
                Serie = factura.Serie?.Trim(),
                Fecha = factura.Fecha,
                Cliente = factura.Nº_Cliente?.Trim(),
                Contacto = factura.Contacto?.Trim(),
                Nombre = nombre,
                Situacion = situacion,
                Estado = factura.VerifactuEstado?.Trim(),
                Motivo = factura.VerifactuUltimoError?.Trim(),
                QueHacer = QueHacer(situacion),
                UltimoIntento = factura.VerifactuUltimoIntento,
                PuedeReintentar = situacion != SITUACION_SIN_DATOS_FISCALES && situacion != SITUACION_INCIDENCIA_ANTIGUA
            };
        }

        /// <summary>
        /// La situación de una factura no registrada (o incorrecta en la AEAT), en palabras de administración.
        /// El orden importa: el rechazo de la AEAT y la exclusión por datos fiscales mandan sobre la incidencia.
        /// </summary>
        internal static string Situacion(CabFacturaVta factura, DateTime hoy)
        {
            if (TieneRegistro(factura))
            {
                return SITUACION_INCORRECTA_AEAT;
            }
            if (factura.VerifactuEstado?.Trim() == VerifactuJobsService.ESTADO_SIN_DATOS_FISCALES)
            {
                return SITUACION_SIN_DATOS_FISCALES;
            }
            if (factura.VerifactuIncidencia == true)
            {
                // Verifacti solo admite el create con incidencia hasta el día siguiente a la fecha (28/09/26)
                return factura.Fecha.Date < hoy.Date.AddDays(-1) ? SITUACION_INCIDENCIA_ANTIGUA : SITUACION_INCIDENCIA;
            }
            return string.IsNullOrWhiteSpace(factura.VerifactuUltimoError) ? SITUACION_PENDIENTE : SITUACION_RECHAZADA_VERIFACTI;
        }

        internal static string QueHacer(string situacion)
        {
            switch (situacion)
            {
                case SITUACION_INCORRECTA_AEAT:
                    return "Corregid lo que dice el motivo (NIF o nombre del cliente: ventana «NIF incorrectos») y pulsad Reintentar: se enviará como subsanación, con el mismo número y la misma fecha.";
                case SITUACION_RECHAZADA_VERIFACTI:
                    return "Corregid lo que dice el motivo (NIF o nombre del cliente: ventana «NIF incorrectos») y pulsad Reintentar. Si no, se reintenta sola cada hora.";
                case SITUACION_INCIDENCIA:
                    return "Verifactu no respondía cuando se emitió. Se reenvía sola cada hora con la marca de incidencia; podéis pulsar Reintentar para no esperar.";
                case SITUACION_INCIDENCIA_ANTIGUA:
                    return "Verifactu no respondía y ya ha pasado más de un día: Verifacti no admite reenviarla. Avisad a Carlos para revisarla a mano.";
                case SITUACION_SIN_DATOS_FISCALES:
                    return "Se creó fuera de Nesto/API sin datos fiscales y no se puede declarar. Avisad a Carlos para revisarla a mano.";
                default:
                    return "Todavía no se ha enviado. Se envía sola cada hora; podéis pulsar Reintentar para no esperar.";
            }
        }

        private async Task<VerifactuResponse> ReenviarConServicioFacturas(CabFacturaVta factura)
        {
            var servicioFacturas = new Facturas.ServicioFacturas(db);
            return RegistroSeriesVerifactu.EsSerieRectificativa(factura.Serie)
                ? await servicioFacturas.EnviarRectificativaAVerifactu(factura.Empresa, factura.Número).ConfigureAwait(false)
                : await servicioFacturas.EnviarFacturaAVerifactu(factura.Empresa, factura.Número).ConfigureAwait(false);
        }

        private async Task<VerifactuResponse> SubsanarConServicioFacturas(CabFacturaVta factura)
        {
            return await new Facturas.ServicioFacturas(db)
                .SubsanarFacturaRechazadaEnVerifactu(factura.Empresa, factura.Número).ConfigureAwait(false);
        }
    }
}
