using System;
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
        // DataTrans exige largo/alto/ancho (obligatorios). No los guardamos por envío, así que cuando
        // no vienen usamos un bulto estándar (la tarifa de Innovatrans es por peso, no volumétrica).
        internal const decimal LARGO_POR_DEFECTO = 30m;
        internal const decimal ALTO_POR_DEFECTO = 30m;
        internal const decimal ANCHO_POR_DEFECTO = 30m;

        private readonly OperacionesEnviosDataTrans _operaciones;
        private readonly DireccionDataTrans _remitente;

        public AgenciaRemotaInnovatrans(OperacionesEnviosDataTrans operaciones, DireccionDataTrans remitente)
        {
            _operaciones = operaciones ?? throw new ArgumentNullException(nameof(operaciones));
            _remitente = remitente ?? throw new ArgumentNullException(nameof(remitente));
        }

        public async Task<ResultadoTramitacionRemota> InsertarYEtiquetarAsync(DatosEnvioRemoto envio)
        {
            if (envio == null) throw new ArgumentNullException(nameof(envio));

            EnvioDataTrans peticion = ConstruirPeticion(envio);
            ResultadoInsertarEnvio insercion = await _operaciones.InsertarEnvioAsync(peticion).ConfigureAwait(false);
            if (!insercion.Exito)
            {
                return new ResultadoTramitacionRemota
                {
                    Exito = false,
                    Error = $"Innovatrans rechazó el envío (codError {insercion.CodError}): {insercion.MsgError}"
                };
            }

            EtiquetaDataTrans etiqueta = await _operaciones
                .BuscarEtiquetaAsync(insercion.Albaran, FormatoEtiquetaDataTrans.Zpl).ConfigureAwait(false);

            return new ResultadoTramitacionRemota
            {
                Exito = true,
                Albaran = insercion.Albaran,
                Bultos = ParsearBultos(insercion.Bultos),
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
