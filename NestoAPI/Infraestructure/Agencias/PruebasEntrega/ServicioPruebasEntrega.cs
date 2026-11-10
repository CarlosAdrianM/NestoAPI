using System;
using System.Threading.Tasks;
using NestoAPI.Infraestructure.Agencias.Perfiles;
using NestoAPI.Models;
using NestoAPI.Models.Agencias;

namespace NestoAPI.Infraestructure.Agencias.PruebasEntrega
{
    /// <summary>
    /// Prueba de entrega por agencia (10/11/26): resuelve el proveedor por la agencia del envío y lo devuelve en la forma común.
    /// Nunca lanza: sin proveedor, sin entregar o con la agencia fallando, <see cref="PruebaEntregaEnvioDTO.Disponible"/> false
    /// con el motivo de usted (lo que falla de verdad queda en ELMAH, sin datos personales).
    /// </summary>
    public class ServicioPruebasEntrega
    {
        public const string MENSAJE_AGENCIA_SIN_PRUEBA = "Esta agencia no da prueba de entrega.";
        public const string MENSAJE_NO_ENTREGADO = "El envío todavía no consta como entregado: no hay prueba de entrega que ver.";
        public const string MENSAJE_SIN_PRUEBA = "La agencia no tiene la prueba de entrega de este envío.";

        private static readonly Lazy<RegistroAgencias> RegistroSinPuerta = new Lazy<RegistroAgencias>(RegistroAgencias.PorReflexionSinPuerta);

        private readonly Func<int, IProveedorPruebaEntrega> proveedorDe;
        private readonly Action<Exception> registrarError;

        /// <param name="proveedorDe">El proveedor de la agencia, o null si no da prueba de entrega.</param>
        public ServicioPruebasEntrega(Func<int, IProveedorPruebaEntrega> proveedorDe, Action<Exception> registrarError = null)
        {
            this.proveedorDe = proveedorDe ?? throw new ArgumentNullException(nameof(proveedorDe));
            this.registrarError = registrarError ?? (_ => { });
        }

        /// <summary>Los perfiles de las agencias (sin puerta de cuarentena) con <see cref="IPerfilConPruebaEntrega"/>.</summary>
        public static ServicioPruebasEntrega DeProduccion(NVEntities db)
            => new ServicioPruebasEntrega(agencia => ProveedorDe(RegistroSinPuerta.Value, agencia, db), RegistrarEnElmah);

        /// <summary>El proveedor que declara el perfil de la agencia; null si no tiene perfil o no da prueba de entrega.</summary>
        internal static IProveedorPruebaEntrega ProveedorDe(RegistroAgencias registro, int agencia, NVEntities db)
            => (registro?.Perfil(agencia) as IPerfilConPruebaEntrega)?.CrearPruebaEntrega(db);

        public async Task<PruebaEntregaEnvioDTO> Obtener(EnviosAgencia envio, string nombreAgencia)
        {
            if (envio == null) throw new ArgumentNullException(nameof(envio));
            string agencia = string.IsNullOrWhiteSpace(nombreAgencia) ? "la agencia" : nombreAgencia.Trim();
            var dto = new PruebaEntregaEnvioDTO
            {
                Numero = envio.Numero,
                Agencia = envio.Agencia,
                NombreAgencia = nombreAgencia?.Trim(),
                Pedido = envio.Pedido
            };

            IProveedorPruebaEntrega proveedor;
            try
            {
                proveedor = proveedorDe(envio.Agencia);
            }
            catch (Exception ex)
            {
                registrarError(new Exception($"Prueba de entrega del envío {envio.Numero}: no se pudo preparar el proveedor de la agencia {envio.Agencia}. {ex.Message}", ex));
                return NoDisponible(dto, $"No se ha podido consultar la prueba de entrega en {agencia}: {ex.Message}");
            }
            if (proveedor == null)
            {
                return NoDisponible(dto, MENSAJE_AGENCIA_SIN_PRUEBA);
            }
            if (envio.Estado != Constantes.Agencias.ESTADO_ENTREGADO)
            {
                return NoDisponible(dto, MENSAJE_NO_ENTREGADO);
            }

            PruebaEntregaAgencia prueba;
            try
            {
                prueba = await proveedor.ObtenerAsync(envio).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                registrarError(new Exception($"Prueba de entrega del envío {envio.Numero} (agencia {envio.Agencia}): {ex.Message}", ex));
                return NoDisponible(dto, $"No se ha podido consultar la prueba de entrega en {agencia}. Inténtelo de nuevo más tarde. ({ex.Message})");
            }
            if (prueba == null)
            {
                return NoDisponible(dto, MENSAJE_SIN_PRUEBA);
            }

            dto.Disponible = prueba.Disponible;
            dto.Mensaje = prueba.Mensaje ?? (prueba.Disponible ? null : MENSAJE_SIN_PRUEBA);
            if (!prueba.Disponible)
            {
                return dto;
            }
            dto.FechaHora = prueba.FechaHora;
            dto.Receptor = prueba.Receptor;
            dto.DniReceptor = prueba.DniReceptor;
            bool hayDocumento = prueba.Documento != null && prueba.Documento.Length > 0;
            dto.Documento = hayDocumento ? prueba.Documento : null;
            dto.TipoDocumento = hayDocumento ? prueba.TipoDocumento : null;
            dto.UrlDocumento = prueba.UrlDocumento;
            dto.CobroReembolso = prueba.CobroReembolso;
            dto.ImporteCobrado = prueba.ImporteCobrado;
            dto.Usuario = prueba.Usuario;
            return dto;
        }

        private static PruebaEntregaEnvioDTO NoDisponible(PruebaEntregaEnvioDTO dto, string mensaje)
        {
            dto.Disponible = false;
            dto.Mensaje = mensaje;
            return dto;
        }

        private static void RegistrarEnElmah(Exception ex)
        {
            try
            {
                ElmahHelper.Log(ex);
            }
            catch
            {
                // El diagnóstico nunca rompe la consulta.
            }
        }
    }
}
