using System;
using System.Threading.Tasks;
using NestoAPI.Infraestructure.Agencias.PruebasEntrega;
using NestoAPI.Models;
using NestoAPI.Models.Agencias;

namespace NestoAPI.Infraestructure.Agencias.RepartoPropio
{
    /// <summary>
    /// Prueba de entrega por agencia (10/11/26): la del reparto propio detrás de la interfaz común. Es la MISMA que dan
    /// GET api/RepartoPropio/Envios/{id}/PruebaEntrega y …/Firma (que siguen igual): lo que registró Ariadna al entregar
    /// (EnviosAgenciaPruebasEntrega) y la firma en PNG como documento.
    /// </summary>
    public class PruebaEntregaRepartoPropio : IProveedorPruebaEntrega
    {
        public const string MENSAJE_SIN_PRUEBA = "Este envío no tiene prueba de entrega.";
        public const string MENSAJE_SIN_FIRMA = "Sin firma (se entregó con DNI).";
        public const string MENSAJE_FIRMA_NO_CARGADA = "No se ha podido cargar la firma. Inténtelo de nuevo más tarde.";

        private readonly ServicioRepartoPropio servicio;

        public PruebaEntregaRepartoPropio(ServicioRepartoPropio servicio)
        {
            this.servicio = servicio ?? throw new ArgumentNullException(nameof(servicio));
        }

        public async Task<PruebaEntregaAgencia> ObtenerAsync(EnviosAgencia envio)
        {
            ResultadoReparto<PruebaEntregaDTO> resultado = await servicio.PruebaEntrega(envio.Numero).ConfigureAwait(false);
            if (resultado.Tipo == TipoResultadoReparto.SinContenido || (resultado.Tipo == TipoResultadoReparto.Ok && resultado.Contenido == null))
            {
                return PruebaEntregaAgencia.NoDisponible(MENSAJE_SIN_PRUEBA);
            }
            if (resultado.Tipo != TipoResultadoReparto.Ok)
            {
                return PruebaEntregaAgencia.NoDisponible(resultado.Mensaje);
            }
            PruebaEntregaDTO prueba = resultado.Contenido;
            var respuesta = new PruebaEntregaAgencia
            {
                Disponible = true,
                FechaHora = prueba.FechaHora,
                Receptor = prueba.Receptor,
                DniReceptor = prueba.DniReceptor,
                CobroReembolso = prueba.CobroReembolso,
                ImporteCobrado = prueba.ImporteCobrado,
                Usuario = prueba.Usuario
            };
            if (!prueba.TieneFirma)
            {
                respuesta.Mensaje = MENSAJE_SIN_FIRMA;
                return respuesta;
            }
            try
            {
                ResultadoReparto<byte[]> firma = await servicio.Firma(envio.Numero).ConfigureAwait(false);
                if (firma.Tipo == TipoResultadoReparto.Ok && firma.Contenido != null && firma.Contenido.Length > 0)
                {
                    respuesta.Documento = firma.Contenido;
                    respuesta.TipoDocumento = "image/png";
                }
                else
                {
                    respuesta.Mensaje = MENSAJE_FIRMA_NO_CARGADA;
                }
            }
            catch (Exception ex)
            {
                // La firma es un extra: los datos de la entrega se enseñan igual (como hacía Nesto).
                try
                {
                    ElmahHelper.Log(new Exception($"Prueba de entrega del envío {envio.Numero}: no se pudo leer la firma. {ex.Message}", ex));
                }
                catch
                {
                    // nunca rompe la consulta
                }
                respuesta.Mensaje = MENSAJE_FIRMA_NO_CARGADA;
            }
            return respuesta;
        }
    }
}
