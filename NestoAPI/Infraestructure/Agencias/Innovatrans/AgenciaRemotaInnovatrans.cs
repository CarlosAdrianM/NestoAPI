using System;
using System.Collections.Generic;
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
        private readonly DireccionDataTrans _remitente;
        private readonly RegistroIntercambiosRemotos _registro;

        public AgenciaRemotaInnovatrans(OperacionesEnviosDataTrans operaciones, DireccionDataTrans remitente,
            RegistroIntercambiosRemotos registro = null)
        {
            _operaciones = operaciones ?? throw new ArgumentNullException(nameof(operaciones));
            _remitente = remitente ?? throw new ArgumentNullException(nameof(remitente));
            // Si no se inyecta registro, uno vacío (Intercambios nunca es null). Para que capture algo,
            // el cliente SOAP tiene que compartir ESTE mismo registro (lo cablea la factory).
            _registro = registro ?? new RegistroIntercambiosRemotos();
        }

        public IReadOnlyList<IntercambioRemoto> Intercambios => _registro.Intercambios;

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
            int bultos = ParsearBultos(insercion.Bultos);

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
