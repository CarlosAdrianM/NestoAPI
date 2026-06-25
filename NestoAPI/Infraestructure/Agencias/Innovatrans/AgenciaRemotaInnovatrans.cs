using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using NestoAPI.Infraestructure.Agencias.Tarifas;

namespace NestoAPI.Infraestructure.Agencias.Innovatrans
{
    /// <summary>
    /// Estrategia de gestión remota de Innovatrans (DataTrans DTX). Traduce un <see cref="DatosEnvioRemoto"/>
    /// agnóstico a una petición DataTrans: fija el remitente (nuestro almacén), deriva el tipoServ de la
    /// zona del CP y normaliza el reembolso (centinela &lt; 0 = no cobrar). El flujo "registrar al imprimir"
    /// = InsertarEnvios (DTX asigna albarán) + BusquedaEtiquetas(ZPL); reimprimir repite solo la etiqueta.
    /// </summary>
    public class AgenciaRemotaInnovatrans : IAgenciaRemota
    {
        // DataTrans marca largo/alto/ancho como obligatorios y NO guardamos dimensiones por envío
        // (solo el peso, que mete el usuario al imprimir). Usamos como defecto la caja MEDIANA, que
        // es la más usada. Las otras cajas habituales son pequeña (16×15×20) y grande (28×40×60);
        // si algún día se elige caja, se pasan en DatosEnvioRemoto y prevalecen sobre este defecto.
        internal const decimal LARGO_POR_DEFECTO = 32m;
        internal const decimal ANCHO_POR_DEFECTO = 23m;
        internal const decimal ALTO_POR_DEFECTO = 29m;

        private readonly OperacionesEnviosDataTrans _operaciones;
        private readonly OperacionesLecturaDataTrans _lectura;
        private readonly DireccionDataTrans _remitente;
        private readonly RegistroIntercambiosRemotos _registro;

        public AgenciaRemotaInnovatrans(OperacionesEnviosDataTrans operaciones, DireccionDataTrans remitente,
            RegistroIntercambiosRemotos registro = null, OperacionesLecturaDataTrans lectura = null)
        {
            _operaciones = operaciones ?? throw new ArgumentNullException(nameof(operaciones));
            _remitente = remitente ?? throw new ArgumentNullException(nameof(remitente));
            // Si no se inyecta registro, uno vacío (Intercambios nunca es null). Para que capture algo,
            // el cliente SOAP tiene que compartir ESTE mismo registro (lo cablea la factory).
            _registro = registro ?? new RegistroIntercambiosRemotos();
            // Operaciones de lectura (ConsultarEstados/Incidencias) para el seguimiento; la factory las
            // cablea con el mismo cliente SOAP. Si no se inyectan, ConsultarSeguimientoAsync no se puede usar.
            _lectura = lectura;
        }

        public IReadOnlyList<IntercambioRemoto> Intercambios => _registro.Intercambios;

        // Innovatrans está recién integrada: logging detallado ON para vigilarla de cerca (estados no
        // contemplados, bultos discrepantes, fallos de tramitación). Poner a false cuando esté rodada
        // (NestoAPI#259).
        public bool LoggingDetallado => true;

        public async Task<ResultadoTramitacionRemota> InsertarYEtiquetarAsync(DatosEnvioRemoto envio)
        {
            if (envio == null) throw new ArgumentNullException(nameof(envio));

            // DataTrans exige pesoReal > 0; si no, rechaza el envío con un fault poco claro. Mejor
            // cortar aquí con un mensaje entendible (el usuario mete el peso al imprimir la etiqueta).
            if (envio.Peso <= 0)
            {
                return new ResultadoTramitacionRemota
                {
                    Exito = false,
                    Error = "Innovatrans necesita el peso del envío (mayor que 0 kg). Indícalo antes de imprimir la etiqueta."
                };
            }

            EnvioDataTrans peticion = ConstruirPeticion(envio);
            ResultadoInsertarEnvio insercion = await _operaciones.InsertarEnvioAsync(peticion).ConfigureAwait(false);
            if (!insercion.Exito)
            {
                // El motivo puede venir en MsgError o, cuando DTX devuelve codError=200 con un fallo
                // interno, como texto de error en el propio campo albarán.
                string motivo = !string.IsNullOrWhiteSpace(insercion.MsgError)
                    ? insercion.MsgError
                    : (!string.IsNullOrWhiteSpace(insercion.Albaran) ? insercion.Albaran : $"codError {insercion.CodError}");
                return new ResultadoTramitacionRemota
                {
                    Exito = false,
                    Error = $"Innovatrans rechazó el envío: {motivo}"
                };
            }

            // A partir de aquí el envío YA existe en la agencia (albarán asignado). Pase lo que pase
            // con la etiqueta, devolvemos SIEMPRE el albarán y los bultos: así el llamante puede
            // persistirlo y un reintento solo reimprime, en vez de reinsertar (envío fantasma + cobro
            // doble). El Exito=false con albarán = "registrado pero sin etiqueta".
            // El <bultos> de la respuesta de InsertarEnvios es POCO FIABLE (DataTrans suele devolverlo
            // vacío -> ParsearBultos cae a 1). El recuento real es el que ENVIAMOS como Paqs (envio.Bultos),
            // que es el que genera las N etiquetas. Nos quedamos con el mayor para no perder bultos
            // (un envío de 3 bultos quedaba persistido como 1).
            int bultosRespuesta = ParsearBultos(insercion.Bultos);
            if (LoggingDetallado)
            {
                LoguearBultosDiscrepantesSiProcede(envio.Bultos, bultosRespuesta, insercion.Albaran);
            }
            int bultos = Math.Max(envio.Bultos, bultosRespuesta);

            EtiquetaDataTrans etiqueta;
            try
            {
                etiqueta = await _operaciones
                    .BuscarEtiquetaAsync(insercion.Albaran, FormatoEtiquetaDataTrans.Zpl).ConfigureAwait(false);
            }
            catch (DataTransException ex)
            {
                return new ResultadoTramitacionRemota
                {
                    Exito = false,
                    Albaran = insercion.Albaran,
                    Bultos = bultos,
                    Error = $"El envío se registró en Innovatrans (albarán {insercion.Albaran}) pero no se pudo obtener la etiqueta: {ex.Message}"
                };
            }

            // La etiqueta tiene que ser ZPL válido: si Innovatrans no tiene ZPL para este envío devuelve
            // un PDF, que es inservible para la Zebra. No lo damos por bueno (mandaría basura a imprimir).
            if (etiqueta == null || !etiqueta.Exito || !etiqueta.EsZpl)
            {
                return new ResultadoTramitacionRemota
                {
                    Exito = false,
                    Albaran = insercion.Albaran,
                    Bultos = bultos,
                    Etiqueta = etiqueta,
                    Error = $"El envío se registró en Innovatrans (albarán {insercion.Albaran}) pero no devolvió una etiqueta ZPL válida"
                        + (string.IsNullOrWhiteSpace(etiqueta?.Error) ? "." : $": {etiqueta.Error}")
                };
            }

            return new ResultadoTramitacionRemota
            {
                Exito = true,
                Albaran = insercion.Albaran,
                Bultos = bultos,
                Etiqueta = etiqueta
            };
        }

        public Task<EtiquetaDataTrans> ReimprimirAsync(string albaran, int? desdeBulto = null, int? hastaBulto = null)
            => _operaciones.BuscarEtiquetaAsync(albaran, FormatoEtiquetaDataTrans.Zpl, desdeBulto, hastaBulto);

        /// <summary>
        /// Seguimiento normalizado de Innovatrans: combina ConsultarEstados (entrega) y
        /// ConsultarIncidencias (incidencia sin resolver). Prioridad: una incidencia abierta manda
        /// (Incidentado), aunque ya conste la entrega; si no, entrega (Entregado + FechaEntrega real);
        /// si no, sigue Tramitado. Aquí vive TODA la interpretación de DataTrans (nombres/códigos).
        /// </summary>
        public async Task<SeguimientoEnvioRemoto> ConsultarSeguimientoAsync(string albaran)
        {
            if (string.IsNullOrWhiteSpace(albaran)) throw new ArgumentNullException(nameof(albaran));
            if (_lectura == null) throw new InvalidOperationException("Innovatrans no tiene operaciones de lectura configuradas para consultar el seguimiento.");

            ResultadoConsultaEstados estados = await _lectura.ConsultarEstadosAsync(albaran).ConfigureAwait(false);
            ResultadoConsultaIncidencias incidencias = await _lectura.ConsultarIncidenciasAsync(albaran).ConfigureAwait(false);

            // El estado se identifica por el NOMBRE (descriptivo); "ENTREG" cubre ENTREGADO/ENTREGA,
            // "DEVUEL"/"DEVOLUC" la devolución a origen.
            EstadoEnvioDataTrans devolucion = estados.Estados
                .Where(e => NombreContiene(e, "DEVUEL", "DEVOLUC"))
                .OrderByDescending(e => e.Numero ?? 0)
                .FirstOrDefault();
            EstadoEnvioDataTrans entrega = estados.Estados
                .Where(e => NombreContiene(e, "ENTREG"))
                .OrderByDescending(e => e.Numero ?? 0)
                .FirstOrDefault();
            IncidenciaDataTrans incidenciaAbierta = incidencias.Incidencias.FirstOrDefault(i => !i.Resuelta);

            // Devuelto a origen es terminal y manda sobre todo lo demás (el paquete ya ha vuelto).
            if (devolucion != null)
            {
                return new SeguimientoEnvioRemoto
                {
                    Estado = EstadoEnvioSeguimiento.Devuelto,
                    Detalle = devolucion.Nombre
                };
            }
            if (incidenciaAbierta != null)
            {
                return new SeguimientoEnvioRemoto
                {
                    Estado = EstadoEnvioSeguimiento.Incidentado,
                    FechaEntrega = ParsearFechaHora(entrega),
                    Detalle = incidenciaAbierta.Nombre
                };
            }
            if (entrega != null)
            {
                return new SeguimientoEnvioRemoto
                {
                    Estado = EstadoEnvioSeguimiento.Entregado,
                    FechaEntrega = ParsearFechaHora(entrega),
                    Detalle = entrega.Nombre
                };
            }
            EstadoEnvioDataTrans ultimo = estados.Estados.OrderByDescending(e => e.Numero ?? 0).FirstOrDefault();
            if (LoggingDetallado)
            {
                LoguearEstadoNoContempladoSiProcede(ultimo, albaran);
            }
            return new SeguimientoEnvioRemoto
            {
                Estado = EstadoEnvioSeguimiento.Tramitado,
                Detalle = ultimo?.Nombre
            };
        }

        // Estados de Innovatrans que YA sabemos que son "en tránsito" y caen bien en el catch-all
        // (-> Tramitado). Cualquier nombre que no sea entrega/devolución/incidencia NI uno de estos se
        // loguea en ELMAH para descubrir estados nuevos que quizá haya que tratar (NestoAPI#259). Es un
        // log de descubrimiento temporal; ELMAH agrupa duplicados, así que no satura.
        private static readonly string[] EstadosEnTransitoConocidos = { "DOCUMENTADO", "EN TRÁNSITO", "EN TRANSITO" };

        // Observabilidad (NestoAPI#259): si DataTrans devuelve en el insert un nº de bultos distinto del
        // que pedimos (Paqs), lo logueamos. Hoy DataTrans suele devolverlo vacío (-> 1) aunque pidamos
        // 2/3, lo que provocaba que se persistiera 1 (casos Estela/Dumapacar/Belén, 25-jun-2026). El log
        // va en try/catch: nunca rompe la tramitación.
        private static void LoguearBultosDiscrepantesSiProcede(int bultosPedidos, int bultosRespuesta, string albaran)
        {
            if (bultosPedidos <= 0 || bultosRespuesta == bultosPedidos)
            {
                return;
            }
            try
            {
                Elmah.ErrorLog.GetDefault(null)?.Log(new Elmah.Error(new Exception(
                    $"Innovatrans: bultos pedidos ({bultosPedidos}) distintos de los de la respuesta del insert " +
                    $"({bultosRespuesta}) en el albarán {albaran}. Se persiste el mayor (NestoAPI#259).")));
            }
            catch
            {
                // El logueo de observabilidad NUNCA debe romper la tramitación.
            }
        }

        private static void LoguearEstadoNoContempladoSiProcede(EstadoEnvioDataTrans estado, string albaran)
        {
            string nombre = estado?.Nombre?.Trim();
            if (string.IsNullOrEmpty(nombre))
            {
                return;
            }
            string nombreUpper = nombre.ToUpperInvariant();
            bool conocido = nombreUpper.Contains("ENTREG") || nombreUpper.Contains("DEVUEL") || nombreUpper.Contains("DEVOLUC")
                || EstadosEnTransitoConocidos.Any(e => nombreUpper.Contains(e));
            if (conocido)
            {
                return;
            }
            try
            {
                Elmah.ErrorLog.GetDefault(null)?.Log(new Elmah.Error(new Exception(
                    $"Estado de Innovatrans no contemplado: '{nombre}' (albarán {albaran}). Revisar si hay que tratarlo (NestoAPI#259).")));
            }
            catch
            {
                // El logueo de descubrimiento NUNCA debe romper el seguimiento.
            }
        }

        // ¿El nombre del estado (en mayúsculas) contiene alguna de las claves? Identifica el tipo de
        // evento por su texto descriptivo (DataTrans no publica un catálogo fijo de códigos).
        private static bool NombreContiene(EstadoEnvioDataTrans estado, params string[] claves)
        {
            string nombre = (estado.Nombre ?? string.Empty).ToUpperInvariant();
            return claves.Any(c => nombre.Contains(c));
        }

        // Combina fecha (dd/MM/yyyy) y hora (HH:mm:ss) de DataTrans en un DateTime; null si no hay fecha.
        private static DateTime? ParsearFechaHora(EstadoEnvioDataTrans estado)
        {
            if (estado == null || string.IsNullOrWhiteSpace(estado.Fecha)) return null;
            string texto = (estado.Fecha.Trim() + " " + (estado.Hora ?? string.Empty).Trim()).Trim();
            string[] formatos = { "dd/MM/yyyy HH:mm:ss", "dd/MM/yyyy HH:mm", "dd/MM/yyyy" };
            foreach (string formato in formatos)
            {
                if (DateTime.TryParseExact(texto, formato, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime fecha))
                {
                    return fecha;
                }
            }
            return null;
        }

        private EnvioDataTrans ConstruirPeticion(DatosEnvioRemoto envio)
        {
            bool esPortugal = CalculadoraZonaEnvio.CalcularZona(envio.CodigoPostal) == ZonasEnvioAgencia.Portugal;

            return new EnvioDataTrans
            {
                Remitente = _remitente,
                Destinatario = new DireccionDataTrans
                {
                    Pais = esPortugal ? MapeadorDireccionDataTrans.PAIS_PORTUGAL : MapeadorDireccionDataTrans.PAIS_ESPANA,
                    Nombre = envio.Nombre,
                    Telefono = string.IsNullOrWhiteSpace(envio.Telefono) ? envio.Movil : envio.Telefono,
                    // CP en crudo: la conversión a formato DataTrans (España tal cual; Portugal comprimido
                    // a "6"+4 dígitos en codPostalDes) la hace el mapeador al construir el XML del envío.
                    CodigoPostal = envio.CodigoPostal,
                    Poblacion = envio.Poblacion,
                    Direccion = envio.Direccion
                },
                Referencia = envio.Referencia,
                TipoServicio = MapeadorTipoServicioDataTrans.TipoServicioDesdeCodigoPostal(envio.CodigoPostal),
                Largo = envio.Largo > 0 ? envio.Largo : LARGO_POR_DEFECTO,
                Alto = envio.Alto > 0 ? envio.Alto : ALTO_POR_DEFECTO,
                Ancho = envio.Ancho > 0 ? envio.Ancho : ANCHO_POR_DEFECTO,
                PesoReal = envio.Peso,
                Docs = 0,
                Paqs = envio.Bultos > 0 ? envio.Bultos : 1,
                Reembolso = envio.Reembolso > 0 ? envio.Reembolso : 0m,
                ComisionReembolsoPagada = false,
                PortesPagados = true,
                Observaciones = envio.Observaciones
            };
        }

        private static int ParsearBultos(string bultos)
            => int.TryParse(bultos, out int valor) && valor > 0 ? valor : 1;
    }
}
