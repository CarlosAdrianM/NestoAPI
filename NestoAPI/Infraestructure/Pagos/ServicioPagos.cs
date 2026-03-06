using Elmah;
using NestoAPI.Infraestructure.Contabilidad;
using NestoAPI.Models;
using NestoAPI.Models.Pagos;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Threading.Tasks;
using System.Web;
using static NestoAPI.Models.Constantes;

namespace NestoAPI.Infraestructure.Pagos
{
    public class ServicioPagos : IServicioPagos
    {
        private readonly IRedsysService _redsysService;
        private readonly IContabilidadService _contabilidadService;
        private readonly ILectorParametrosUsuario _lectorParametros;

        public ServicioPagos(IRedsysService redsysService, IContabilidadService contabilidadService, ILectorParametrosUsuario lectorParametros)
        {
            _redsysService = redsysService;
            _contabilidadService = contabilidadService;
            _lectorParametros = lectorParametros;
        }

        public async Task<RespuestaIniciarPago> IniciarPago(SolicitudPagoTPV solicitud, string usuario)
        {
            if (solicitud.Importe <= 0)
            {
                throw new ArgumentException("El importe debe ser mayor que cero");
            }

            string urlBase = "https://api.nuevavision.es";
            string urlNotificacion = urlBase + "/api/Pagos/NotificacionRedsys";
            string urlOk = solicitud.UrlOk ?? urlBase + "/pago/ok.html";
            string urlKo = solicitud.UrlKo ?? urlBase + "/pago/ko.html";

            ParametrosRedsysFirmados parametros = _redsysService.CrearParametrosTPVVirtual(
                solicitud.Importe,
                solicitud.Descripcion,
                solicitud.Correo,
                solicitud.Cliente,
                urlNotificacion,
                urlOk,
                urlKo);

            using (NVEntities db = new NVEntities())
            {
                var pago = new PagoTPV
                {
                    NumeroOrden = parametros.NumeroOrden,
                    Tipo = "TPVVirtual",
                    Empresa = solicitud.Empresa ?? Empresas.EMPRESA_POR_DEFECTO,
                    Cliente = solicitud.Cliente,
                    Contacto = solicitud.Contacto,
                    Importe = solicitud.Importe,
                    Descripcion = solicitud.Descripcion,
                    Correo = solicitud.Correo,
                    ExtractoClienteId = solicitud.ExtractoClienteId,
                    Documento = solicitud.Documento,
                    Efecto = solicitud.Efecto,
                    Vendedor = solicitud.Vendedor,
                    FormaVenta = solicitud.FormaVenta,
                    Delegacion = solicitud.Delegacion,
                    TipoApunte = solicitud.TipoApunte,
                    Estado = "Pendiente",
                    FechaCreacion = DateTime.Now,
                    Usuario = usuario
                };

                db.PagosTPV.Add(pago);
                await db.SaveChangesAsync().ConfigureAwait(false);

                return new RespuestaIniciarPago
                {
                    IdPago = pago.Id,
                    UrlRedsys = _redsysService.UrlFormularioRedsys,
                    Ds_SignatureVersion = parametros.Ds_SignatureVersion,
                    Ds_MerchantParameters = parametros.Ds_MerchantParameters,
                    Ds_Signature = parametros.Ds_Signature
                };
            }
        }

        public async Task<bool> ProcesarNotificacion(NotificacionRedsys notificacion)
        {
            ResultadoValidacionNotificacion resultado = _redsysService.ValidarNotificacion(notificacion);

            if (!resultado.FirmaValida)
            {
                LogElmah($"[ProcesarNotificacion] Firma inválida. Orden: {resultado.NumeroOrden}, Error: {resultado.MensajeError}");
                return false;
            }

            using (NVEntities db = new NVEntities())
            {
                PagoTPV pago = await db.PagosTPV
                    .FirstOrDefaultAsync(p => p.NumeroOrden == resultado.NumeroOrden)
                    .ConfigureAwait(false);

                if (pago == null)
                {
                    LogElmah($"[ProcesarNotificacion] Pago no encontrado. Orden: {resultado.NumeroOrden}");
                    return false;
                }

                pago.CodigoRespuesta = resultado.CodigoRespuesta;
                pago.CodigoAutorizacion = resultado.CodigoAutorizacion;
                pago.FechaActualizacion = DateTime.Now;

                if (resultado.PagoAutorizado)
                {
                    pago.Estado = "Autorizado";
                    await db.SaveChangesAsync().ConfigureAwait(false);

                    // Contabilizar el cobro
                    await ContabilizarCobro(pago).ConfigureAwait(false);
                }
                else
                {
                    pago.Estado = "Denegado";
                    await db.SaveChangesAsync().ConfigureAwait(false);
                }

                return true;
            }
        }

        private async Task ContabilizarCobro(PagoTPV pago)
        {
            if (string.IsNullOrWhiteSpace(pago.Cliente))
            {
                return;
            }

            string cuentaBanco = _lectorParametros.LeerParametro(
                pago.Empresa?.Trim() ?? Empresas.EMPRESA_POR_DEFECTO,
                "(defecto)",
                "CuentaBancoTarjeta");

            if (string.IsNullOrWhiteSpace(cuentaBanco))
            {
                return;
            }

            string empresa = pago.Empresa?.Trim() ?? Empresas.EMPRESA_POR_DEFECTO;
            string delegacion = pago.Delegacion?.Trim() ?? "ALG";
            string formaVenta = pago.FormaVenta?.Trim() ?? Constantes.FormasVenta.TIENDA_ONLINE;
            string documento = pago.Documento?.Trim();
            if (string.IsNullOrWhiteSpace(documento))
            {
                // Fallback: últimos 10 chars del NumeroOrden
                documento = pago.NumeroOrden?.Length > 10
                    ? pago.NumeroOrden.Substring(pago.NumeroOrden.Length - 10)
                    : pago.NumeroOrden;
            }
            string concepto = $"Pago TPV {pago.Descripcion}";
            if (concepto.Length > 50)
            {
                concepto = concepto.Substring(0, 50);
            }

            var lineas = new List<PreContabilidad>
            {
                // Línea banco (DEBE)
                new PreContabilidad
                {
                    Empresa = empresa,
                    Nº_Cuenta = cuentaBanco,
                    TipoCuenta = Constantes.Contabilidad.TiposCuenta.CUENTA_CONTABLE,
                    TipoApunte = TiposExtractoCliente.PAGO,
                    Debe = pago.Importe,
                    Concepto = concepto,
                    Nº_Documento = documento,
                    Diario = "_CobrosTPV",
                    Fecha = DateTime.Today,
                    FechaVto = DateTime.Today,
                    Asiento = 1,
                    Asiento_Automático = true,
                    Delegación = delegacion,
                    FormaVenta = formaVenta,
                    FormaPago = Constantes.FormasPago.TARJETA,
                    Origen = Empresas.EMPRESA_POR_DEFECTO,
                    Usuario = "NestoAPI",
                    Fecha_Modificación = DateTime.Now
                },
                // Línea cliente (HABER)
                new PreContabilidad
                {
                    Empresa = empresa,
                    Nº_Cuenta = pago.Cliente,
                    Contacto = pago.Contacto ?? "0",
                    TipoCuenta = Constantes.Contabilidad.TiposCuenta.CLIENTE,
                    TipoApunte = TiposExtractoCliente.PAGO,
                    Haber = pago.Importe,
                    Concepto = concepto,
                    Nº_Documento = documento,
                    Efecto = pago.Efecto,
                    Diario = "_CobrosTPV",
                    Fecha = DateTime.Today,
                    FechaVto = DateTime.Today,
                    Asiento = 1,
                    Asiento_Automático = true,
                    Delegación = delegacion,
                    FormaVenta = formaVenta,
                    FormaPago = Constantes.FormasPago.TARJETA,
                    Vendedor = pago.Vendedor,
                    Liquidado = pago.ExtractoClienteId,
                    Origen = Empresas.EMPRESA_POR_DEFECTO,
                    Usuario = "NestoAPI",
                    Fecha_Modificación = DateTime.Now
                }
            };

            await _contabilidadService.CrearLineasYContabilizarDiario(lineas).ConfigureAwait(false);
        }

        public async Task<PagoTPVDTO> ConsultarPago(int idPago)
        {
            using (NVEntities db = new NVEntities())
            {
                PagoTPV pago = await db.PagosTPV
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Id == idPago)
                    .ConfigureAwait(false);

                return pago != null ? MapearADTO(pago) : null;
            }
        }

        public async Task<PagoTPVDTO> ConsultarAuditoria(string numeroOrden)
        {
            using (NVEntities db = new NVEntities())
            {
                PagoTPV pago = await db.PagosTPV
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.NumeroOrden == numeroOrden)
                    .ConfigureAwait(false);

                return pago != null ? MapearADTO(pago) : null;
            }
        }

        internal static PagoTPVDTO MapearADTO(PagoTPV pago)
        {
            return new PagoTPVDTO
            {
                Id = pago.Id,
                NumeroOrden = pago.NumeroOrden,
                Tipo = pago.Tipo,
                Empresa = pago.Empresa?.Trim(),
                Cliente = pago.Cliente?.Trim(),
                Contacto = pago.Contacto?.Trim(),
                Importe = pago.Importe,
                Descripcion = pago.Descripcion,
                Correo = pago.Correo,
                Movil = pago.Movil,
                Estado = pago.Estado,
                CodigoRespuesta = pago.CodigoRespuesta,
                CodigoAutorizacion = pago.CodigoAutorizacion,
                FechaCreacion = pago.FechaCreacion,
                FechaActualizacion = pago.FechaActualizacion,
                Usuario = pago.Usuario,
                ExtractoClienteId = pago.ExtractoClienteId,
                Documento = pago.Documento?.Trim(),
                Efecto = pago.Efecto?.Trim(),
                Vendedor = pago.Vendedor?.Trim(),
                FormaVenta = pago.FormaVenta?.Trim(),
                Delegacion = pago.Delegacion?.Trim(),
                TipoApunte = pago.TipoApunte?.Trim()
            };
        }

        private static void LogElmah(string mensaje)
        {
            try
            {
                ErrorSignal.FromCurrentContext().Raise(new Exception(mensaje));
            }
            catch
            {
                // No bloquear si falla el logging
            }
        }
    }
}
