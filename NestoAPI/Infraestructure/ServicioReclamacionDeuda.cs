using NestoAPI.Infraestructure.Pagos;
using NestoAPI.Models;
using NestoAPI.Models.Pagos;
using Microsoft.ApplicationInsights;
using System;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure
{
    public interface IServicioReclamacionDeuda
    {
        Task<ReclamacionDeuda> ProcesarReclamacionDeuda(ReclamacionDeuda reclamacion, string usuario = null);
    }

    public class ServicioReclamacionDeuda : IServicioReclamacionDeuda
    {
        private readonly IRedsysService _redsysService;

        public ServicioReclamacionDeuda(IRedsysService redsysService)
        {
            _redsysService = redsysService;
        }

        public async Task<ReclamacionDeuda> ProcesarReclamacionDeuda(ReclamacionDeuda reclamacion, string usuario = null)
        {
            var datosCorreo = new FormatoCorreoReclamacion
            {
                nombreComprador = reclamacion.Nombre,
                direccionComprador = reclamacion.Direccion,
                subjectMailCliente = reclamacion.Asunto,
                textoLibre1 = "Utilice el siguiente botón para pagar la deuda pendiente con NUEVA VISION"
            };

            ParametrosRedsysFirmados parametros = _redsysService.CrearParametrosP2F(
                reclamacion.Importe,
                reclamacion.Correo,
                reclamacion.Movil,
                reclamacion.TextoSMS,
                reclamacion.Cliente,
                datosCorreo);

            RespuestaRedsys respuesta = await _redsysService.EnviarPeticionREST(parametros).ConfigureAwait(false);

            if (respuesta != null)
            {
                reclamacion.Enlace = respuesta.Ds_UrlPago2Fases;
                reclamacion.TramitadoOK = true;

                await RegistrarAuditoria(parametros, reclamacion, usuario).ConfigureAwait(false);
            }

            TelemetryClient telemetry = new TelemetryClient();
            telemetry.TrackEvent("EnvioPagoTarjeta");

            return reclamacion;
        }

        private async Task RegistrarAuditoria(ParametrosRedsysFirmados parametros, ReclamacionDeuda reclamacion, string usuario)
        {
            try
            {
                using (NVEntities db = new NVEntities())
                {
                    var pago = new PagoTPV
                    {
                        NumeroOrden = parametros.NumeroOrden,
                        Tipo = "P2F",
                        Empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO,
                        Cliente = reclamacion.Cliente,
                        Importe = reclamacion.Importe,
                        Descripcion = reclamacion.Asunto,
                        Correo = reclamacion.Correo,
                        Movil = reclamacion.Movil,
                        Estado = "Enviado",
                        FechaCreacion = DateTime.Now,
                        Usuario = usuario
                    };

                    db.PagosTPV.Add(pago);
                    await db.SaveChangesAsync().ConfigureAwait(false);
                }
            }
            catch (Exception)
            {
                // No bloquear el flujo principal si falla la auditoría
            }
        }
    }
}
