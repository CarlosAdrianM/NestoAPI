using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NestoAPI.Infraestructure.Agencias.Innovatrans;
using NestoAPI.Models;

namespace NestoAPI.Infraestructure.Agencias.RepartoPropio
{
    /// <summary>
    /// NestoAPI#739: el reparto propio con la misma forma que las agencias de fuera (<see cref="IAgenciaRemota"/>), para que
    /// Tramitar, ImprimirEtiqueta, Anular y Modificar de EnviosAgencias, y la etiqueta F8 del packing de Ariadna, funcionen sin
    /// saber que no hay webservice. «Registrar» = dar el albarán <c>RP{envío}</c> con sus bultos; la etiqueta la genera
    /// <see cref="EtiquetaRepartoPropio"/>. Anular y modificar no tienen a nadie a quien avisar: siempre bien.
    /// </summary>
    public class AgenciaRemotaRepartoPropio : IAgenciaRemota
    {
        private readonly Func<int, Task<EnviosAgencia>> leerEnvio;
        private readonly Func<DateTime> hoy;

        /// <param name="leerEnvio">El envío por su número (para reimprimir, que solo recibe el albarán).</param>
        public AgenciaRemotaRepartoPropio(Func<int, Task<EnviosAgencia>> leerEnvio, Func<DateTime> hoy = null)
        {
            this.leerEnvio = leerEnvio ?? throw new ArgumentNullException(nameof(leerEnvio));
            this.hoy = hoy ?? (() => DateTime.Today);
        }

        public bool LoggingDetallado => false;

        public bool ModificarCreaEnvioNuevo => false;

        public IReadOnlyList<IntercambioRemoto> Intercambios { get; } = new List<IntercambioRemoto>();

        public Task<ResultadoTramitacionRemota> InsertarYEtiquetarAsync(DatosEnvioRemoto envio)
            => Task.FromResult(Registrar(envio));

        public Task<ResultadoTramitacionRemota> ModificarYEtiquetarAsync(DatosEnvioRemoto envio, string albaran)
            => Task.FromResult(Registrar(envio));

        public async Task<EtiquetaDataTrans> ReimprimirAsync(string albaran, int? desdeBulto = null, int? hastaBulto = null)
        {
            if (!CodigoBultoRepartoPropio.TryLeerAlbaran(albaran, out int numero))
            {
                return new EtiquetaDataTrans { Error = $"«{albaran}» no es un albarán del reparto propio (RP y el número del envío)." };
            }
            EnviosAgencia envio = await leerEnvio(numero).ConfigureAwait(false);
            if (envio == null || envio.Agencia != Constantes.Agencias.AGENCIA_REPARTO_PROPIO)
            {
                return new EtiquetaDataTrans { Error = $"El envío {numero} no es del reparto propio." };
            }
            DatosEnvioRemoto datos = DatosEnvioRemoto.DesdeEnvio(envio);
            return EtiquetaRepartoPropio.ComoEtiqueta(EtiquetaRepartoPropio.ConstruirZpl(datos, FechaEtiqueta(datos), desdeBulto, hastaBulto));
        }

        public Task<ResultadoOperacionRemota> AnularAsync(string albaran)
            => Task.FromResult(new ResultadoOperacionRemota { Exito = true });

        /// <summary>No se consulta a nadie: los estados los pone el repartidor desde Ariadna (api/RepartoPropio). Nunca se persiste.</summary>
        public Task<SeguimientoEnvioRemoto> ConsultarSeguimientoAsync(string albaran)
            => Task.FromResult(new SeguimientoEnvioRemoto { Estado = EstadoEnvioSeguimiento.Desconocido, Detalle = "Reparto propio: el estado lo da el repartidor" });

        private ResultadoTramitacionRemota Registrar(DatosEnvioRemoto envio)
        {
            if (envio == null || envio.NumeroEnvio <= 0)
            {
                return new ResultadoTramitacionRemota { Exito = false, Error = "El envío no tiene número: no se pueden numerar sus bultos." };
            }
            int bultos = envio.Bultos > 0 ? envio.Bultos : 1;
            envio.Bultos = bultos;
            return new ResultadoTramitacionRemota
            {
                Exito = true,
                Albaran = CodigoBultoRepartoPropio.Albaran(envio.NumeroEnvio),
                Bultos = bultos,
                Etiqueta = EtiquetaRepartoPropio.ComoEtiqueta(EtiquetaRepartoPropio.ConstruirZpl(envio, FechaEtiqueta(envio)))
            };
        }

        // La fecha del envío (la del picking) o, si es de un día pasado o no la tiene, hoy.
        private DateTime FechaEtiqueta(DatosEnvioRemoto envio)
            => envio.FechaRecogida.HasValue && envio.FechaRecogida.Value.Date >= hoy().Date ? envio.FechaRecogida.Value.Date : hoy().Date;
    }
}
