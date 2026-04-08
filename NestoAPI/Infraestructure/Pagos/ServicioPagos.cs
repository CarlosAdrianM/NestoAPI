using NestoAPI.Infraestructure.Contabilidad;
using NestoAPI.Models;
using NestoAPI.Models.Pagos;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Net.Mail;
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
        private readonly IServicioCorreoElectronico _servicioCorreo;
        private readonly ILogService _logService;

        public ServicioPagos(IRedsysService redsysService, IContabilidadService contabilidadService, ILectorParametrosUsuario lectorParametros)
            : this(redsysService, contabilidadService, lectorParametros, new ServicioCorreoElectronico(), new ElmahLogService())
        {
        }

        public ServicioPagos(IRedsysService redsysService, IContabilidadService contabilidadService, ILectorParametrosUsuario lectorParametros, IServicioCorreoElectronico servicioCorreo)
            : this(redsysService, contabilidadService, lectorParametros, servicioCorreo, new ElmahLogService())
        {
        }

        public ServicioPagos(IRedsysService redsysService, IContabilidadService contabilidadService, ILectorParametrosUsuario lectorParametros, IServicioCorreoElectronico servicioCorreo, ILogService logService)
        {
            _redsysService = redsysService;
            _contabilidadService = contabilidadService;
            _lectorParametros = lectorParametros;
            _servicioCorreo = servicioCorreo;
            _logService = logService;
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

            // Normalizar: si vienen campos legacy sin Efectos, crear un efecto a partir de ellos
            List<EfectoAPagar> efectos = NormalizarEfectos(solicitud);

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
                    // Campos legacy se mantienen para compatibilidad
                    ExtractoClienteId = solicitud.ExtractoClienteId,
                    Documento = solicitud.Documento,
                    Efecto = solicitud.Efecto,
                    Vendedor = solicitud.Vendedor,
                    FormaVenta = solicitud.FormaVenta,
                    Delegacion = solicitud.Delegacion,
                    TipoApunte = solicitud.TipoApunte,
                    Estado = Constantes.EstadosPagoTPV.PENDIENTE,
                    FechaCreacion = DateTime.Now,
                    Usuario = usuario,
                    TokenAcceso = Guid.NewGuid()
                };

                db.PagosTPV.Add(pago);
                await db.SaveChangesAsync().ConfigureAwait(false);

                foreach (var efecto in efectos)
                {
                    var pagoEfecto = new PagoTPV_Efecto
                    {
                        IdPago = pago.Id,
                        ExtractoClienteId = efecto.ExtractoClienteId,
                        Importe = efecto.Importe,
                        Documento = efecto.Documento,
                        Efecto = efecto.Efecto,
                        Contacto = efecto.Contacto,
                        Vendedor = efecto.Vendedor,
                        FormaVenta = efecto.FormaVenta,
                        Delegacion = efecto.Delegacion,
                        TipoApunte = efecto.TipoApunte
                    };
                    db.PagosTPV_Efectos.Add(pagoEfecto);
                }

                await db.SaveChangesAsync().ConfigureAwait(false);

                string urlPaginaPago = $"https://api.nuevavision.es/pago/{pago.TokenAcceso}";

                // Issue #139: Correo pre-cobro al cliente
                EnviarCorreoPreCobro(pago, efectos, urlPaginaPago);

                return new RespuestaIniciarPago
                {
                    IdPago = pago.Id,
                    UrlRedsys = _redsysService.UrlFormularioRedsys,
                    Ds_SignatureVersion = parametros.Ds_SignatureVersion,
                    Ds_MerchantParameters = parametros.Ds_MerchantParameters,
                    Ds_Signature = parametros.Ds_Signature,
                    TokenAcceso = pago.TokenAcceso,
                    UrlPaginaPago = urlPaginaPago
                };
            }
        }

        public async Task<bool> ProcesarNotificacion(NotificacionRedsys notificacion)
        {
            ResultadoValidacionNotificacion resultado = _redsysService.ValidarNotificacion(notificacion);

            if (!resultado.FirmaValida)
            {
                string mensajeFirma = $"[ProcesarNotificacion] Firma inválida. Orden: {resultado.NumeroOrden}, Error: {resultado.MensajeError}";
                _logService.LogError(mensajeFirma);
                EnviarCorreoAlertaPago("Firma invalida en notificacion Redsys", mensajeFirma, resultado);
                return false;
            }

            using (NVEntities db = new NVEntities())
            {
                PagoTPV pago = await db.PagosTPV
                    .Include(p => p.PagosTPV_Efectos)
                    .FirstOrDefaultAsync(p => p.NumeroOrden == resultado.NumeroOrden)
                    .ConfigureAwait(false);

                if (pago == null)
                {
                    string mensajeNoEncontrado = $"[ProcesarNotificacion] Pago no encontrado. " +
                        $"Orden: {resultado.NumeroOrden}, " +
                        $"Codigo respuesta: {resultado.CodigoRespuesta}, " +
                        $"Codigo autorizacion: {resultado.CodigoAutorizacion}, " +
                        $"Pago autorizado: {resultado.PagoAutorizado}";
                    _logService.LogError(mensajeNoEncontrado);
                    EnviarCorreoAlertaPago(
                        "Pago Redsys recibido pero NO encontrado en base de datos",
                        mensajeNoEncontrado,
                        resultado);
                    return false;
                }

                pago.CodigoRespuesta = resultado.CodigoRespuesta;
                pago.CodigoAutorizacion = resultado.CodigoAutorizacion;
                pago.FechaActualizacion = DateTime.Now;

                if (resultado.PagoAutorizado)
                {
                    pago.Estado = Constantes.EstadosPagoTPV.AUTORIZADO;
                    await db.SaveChangesAsync().ConfigureAwait(false);

                    // Issue #143: Contabilizar con resiliencia - si falla, el correo debe enviarse igualmente
                    string errorContabilizacion = null;
                    try
                    {
                        await ContabilizarCobro(pago).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        errorContabilizacion = ObtenerMensajeCompletoExcepcion(ex);
                        _logService.LogError($"[ProcesarNotificacion] Error al contabilizar cobro. Orden: {pago.NumeroOrden}, Error: {errorContabilizacion}", ex);
                    }

                    // Issue #139/#142/#143: Correo post-cobro a administración (siempre, incluso si falló la contabilización)
                    EnviarCorreoPostCobro(pago, errorContabilizacion);
                }
                else
                {
                    pago.Estado = Constantes.EstadosPagoTPV.DENEGADO;
                    await db.SaveChangesAsync().ConfigureAwait(false);

                    // Issue #156: Regenerar enlace de pago si no se ha superado el límite de reintentos
                    try
                    {
                        await RegenerarPagoDenegado(pago, db).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logService.LogError($"[ProcesarNotificacion] Error al regenerar pago denegado. Orden: {pago.NumeroOrden}, Error: {ex.Message}", ex);
                    }
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
                Parametros.Claves.CuentaBancoTarjeta);

            if (string.IsNullOrWhiteSpace(cuentaBanco))
            {
                return;
            }

            string empresa = pago.Empresa?.Trim() ?? Empresas.EMPRESA_POR_DEFECTO;
            string concepto = $"Pago TPV {pago.Descripcion}";
            if (concepto.Length > 50)
            {
                concepto = concepto.Substring(0, 50);
            }

            var lineas = new List<PreContabilidad>();

            if (pago.PagosTPV_Efectos != null && pago.PagosTPV_Efectos.Any())
            {
                // Pagos multiples: una linea HABER por cada efecto
                foreach (var efecto in pago.PagosTPV_Efectos)
                {
                    string docEfecto = efecto.Documento?.Trim();
                    if (string.IsNullOrWhiteSpace(docEfecto))
                    {
                        docEfecto = pago.NumeroOrden?.Length > 10
                            ? pago.NumeroOrden.Substring(pago.NumeroOrden.Length - 10)
                            : pago.NumeroOrden;
                    }

                    lineas.Add(new PreContabilidad
                    {
                        Empresa = empresa,
                        Nº_Cuenta = pago.Cliente,
                        Contacto = efecto.Contacto?.Trim() ?? pago.Contacto ?? "0",
                        TipoCuenta = Constantes.Contabilidad.TiposCuenta.CLIENTE,
                        TipoApunte = TiposExtractoCliente.PAGO,
                        Haber = efecto.Importe,
                        Concepto = concepto,
                        Nº_Documento = docEfecto,
                        Efecto = efecto.Efecto?.Trim(),
                        Diario = "_CobrosTPV",
                        Fecha = DateTime.Today,
                        FechaVto = DateTime.Today,
                        Asiento = 1,
                        Asiento_Automático = true,
                        Delegación = efecto.Delegacion?.Trim() ?? "ALG",
                        FormaVenta = efecto.FormaVenta?.Trim() ?? Constantes.FormasVenta.TIENDA_ONLINE,
                        FormaPago = Constantes.FormasPago.TARJETA,
                        Vendedor = efecto.Vendedor?.Trim(),
                        Liquidado = efecto.ExtractoClienteId,
                        Origen = Empresas.EMPRESA_POR_DEFECTO,
                        Usuario = "NestoAPI",
                        Fecha_Modificación = DateTime.Now
                    });
                }
            }
            else
            {
                // Pago individual legacy (sin tabla de efectos)
                string delegacion = pago.Delegacion?.Trim() ?? "ALG";
                string formaVenta = pago.FormaVenta?.Trim() ?? Constantes.FormasVenta.TIENDA_ONLINE;
                string documento = pago.Documento?.Trim();
                if (string.IsNullOrWhiteSpace(documento))
                {
                    documento = pago.NumeroOrden?.Length > 10
                        ? pago.NumeroOrden.Substring(pago.NumeroOrden.Length - 10)
                        : pago.NumeroOrden;
                }

                lineas.Add(new PreContabilidad
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
                });
            }

            // Si hay Liquidado, copiar Nº_Documento, Vendedor y Ruta del movimiento original
            var lineasConLiquidado = lineas.Where(l => l.Liquidado.HasValue).ToList();
            if (lineasConLiquidado.Any())
            {
                var numerosOrden = lineasConLiquidado.Select(l => l.Liquidado.Value).Distinct().ToList();
                using (NVEntities db = new NVEntities())
                {
                    var movimientosOriginales = await db.ExtractosCliente
                        .Where(e => e.Empresa == empresa && numerosOrden.Contains(e.Nº_Orden))
                        .ToListAsync()
                        .ConfigureAwait(false);

                    foreach (var linea in lineasConLiquidado)
                    {
                        var original = movimientosOriginales.FirstOrDefault(e => e.Nº_Orden == linea.Liquidado.Value);
                        if (original != null)
                        {
                            linea.Nº_Documento = original.Nº_Documento?.Trim();
                            linea.Vendedor = original.Vendedor?.Trim();
                            linea.Ruta = original.Ruta?.Trim();
                        }
                    }
                }
            }

            // Linea banco (DEBE) - siempre una sola linea por el total
            string docBanco = pago.NumeroOrden?.Length > 10
                ? pago.NumeroOrden.Substring(pago.NumeroOrden.Length - 10)
                : pago.NumeroOrden;

            // Si hay líneas con Liquidado, usar el Nº_Documento del movimiento original (como hace Cajas)
            var lineaConDocumentoOriginal = lineas.FirstOrDefault(l => l.Liquidado.HasValue && !string.IsNullOrWhiteSpace(l.Nº_Documento));
            string docBancoFinal = lineaConDocumentoOriginal?.Nº_Documento ?? docBanco;

            lineas.Insert(0, new PreContabilidad
            {
                Empresa = empresa,
                Nº_Cuenta = cuentaBanco,
                TipoCuenta = Constantes.Contabilidad.TiposCuenta.CUENTA_CONTABLE,
                TipoApunte = TiposExtractoCliente.PAGO,
                Debe = pago.Importe,
                Concepto = concepto,
                Nº_Documento = docBancoFinal,
                Diario = "_CobrosTPV",
                Fecha = DateTime.Today,
                FechaVto = DateTime.Today,
                Asiento = 1,
                Asiento_Automático = true,
                Delegación = "ALG",
                FormaVenta = Constantes.FormasVenta.TIENDA_ONLINE,
                FormaPago = Constantes.FormasPago.TARJETA,
                Origen = Empresas.EMPRESA_POR_DEFECTO,
                Usuario = "NestoAPI",
                Fecha_Modificación = DateTime.Now
            });

            await _contabilidadService.CrearLineasYContabilizarDiario(lineas).ConfigureAwait(false);
        }

        internal static List<EfectoAPagar> NormalizarEfectos(SolicitudPagoTPV solicitud)
        {
            if (solicitud.Efectos != null && solicitud.Efectos.Any())
            {
                return solicitud.Efectos;
            }

            // Compatibilidad: si no hay Efectos pero hay ExtractoClienteId, crear uno
            if (solicitud.ExtractoClienteId.HasValue)
            {
                return new List<EfectoAPagar>
                {
                    new EfectoAPagar
                    {
                        ExtractoClienteId = solicitud.ExtractoClienteId.Value,
                        Importe = solicitud.Importe,
                        Documento = solicitud.Documento,
                        Efecto = solicitud.Efecto,
                        Contacto = solicitud.Contacto,
                        Vendedor = solicitud.Vendedor,
                        FormaVenta = solicitud.FormaVenta,
                        Delegacion = solicitud.Delegacion,
                        TipoApunte = solicitud.TipoApunte
                    }
                };
            }

            return new List<EfectoAPagar>();
        }

        public async Task<PagoTPVDTO> ConsultarPago(int idPago)
        {
            using (NVEntities db = new NVEntities())
            {
                PagoTPV pago = await db.PagosTPV
                    .Include(p => p.PagosTPV_Efectos)
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
                    .Include(p => p.PagosTPV_Efectos)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.NumeroOrden == numeroOrden)
                    .ConfigureAwait(false);

                return pago != null ? MapearADTO(pago) : null;
            }
        }

        public async Task<List<PagoTPVDTO>> ListarPorCliente(string empresa, string cliente, int limite = 20)
        {
            using (NVEntities db = new NVEntities())
            {
                string empresaPadded = empresa.PadRight(3);
                var pagos = await db.PagosTPV
                    .Include(p => p.PagosTPV_Efectos)
                    .AsNoTracking()
                    .Where(p => p.Empresa == empresaPadded && p.Cliente == cliente)
                    .OrderByDescending(p => p.FechaCreacion)
                    .Take(limite)
                    .ToListAsync()
                    .ConfigureAwait(false);

                return pagos.Select(p => MapearADTO(p)).ToList();
            }
        }

        internal static PagoTPVDTO MapearADTO(PagoTPV pago)
        {
            var dto = new PagoTPVDTO
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
                TipoApunte = pago.TipoApunte?.Trim(),
                PagoOriginalId = pago.PagoOriginalId
            };

            if (pago.PagosTPV_Efectos != null && pago.PagosTPV_Efectos.Any())
            {
                dto.Efectos = pago.PagosTPV_Efectos.Select(e => new EfectoTPVDTO
                {
                    Id = e.Id,
                    ExtractoClienteId = e.ExtractoClienteId,
                    Importe = e.Importe,
                    Documento = e.Documento?.Trim(),
                    Efecto = e.Efecto?.Trim(),
                    Contacto = e.Contacto?.Trim(),
                    Vendedor = e.Vendedor?.Trim(),
                    FormaVenta = e.FormaVenta?.Trim(),
                    Delegacion = e.Delegacion?.Trim(),
                    TipoApunte = e.TipoApunte?.Trim()
                }).ToList();
            }

            return dto;
        }

        private const string URL_LOGO = "https://www.productosdeesteticaypeluqueriaprofesional.com/img/cms/Landing/logo.png";

        /// <summary>
        /// Envía correo al cliente con el enlace de pago generado.
        /// Issue #139: Correo pre-cobro. Si el correo es null, no envía.
        /// </summary>
        internal void EnviarCorreoPreCobro(PagoTPV pago, List<EfectoAPagar> efectos, string urlPaginaPago)
        {
            if (string.IsNullOrWhiteSpace(pago.Correo))
            {
                return;
            }

            try
            {
                string filasEfectos = "";
                bool alternar = false;
                if (efectos != null && efectos.Any())
                {
                    foreach (var e in efectos)
                    {
                        string bgColor = alternar ? "background-color:#faf5f7;" : "";
                        filasEfectos +=
                            $"<tr style='{bgColor}'>" +
                            $"<td style='padding:10px;border-bottom:1px solid #f0e8ec'>{e.Documento?.Trim()}</td>" +
                            $"<td style='padding:10px;border-bottom:1px solid #f0e8ec;text-align:right;white-space:nowrap'>{e.Importe:N2} &euro;</td></tr>";
                        alternar = !alternar;
                    }
                }

                string seccionEfectos = efectos != null && efectos.Any()
                    ? $@"<table style='border-collapse:collapse;width:100%;margin:20px 0'>
                        <tr style='background:#f8f4f6'>
                            <th style='padding:10px;text-align:left;border-bottom:2px solid #d4a5b5;color:#6b3a5d'>Documento</th>
                            <th style='padding:10px;text-align:right;border-bottom:2px solid #d4a5b5;color:#6b3a5d'>Importe</th>
                        </tr>
                        {filasEfectos}
                        <tr style='background:#f8f4f6'>
                            <td style='padding:10px;font-weight:bold;color:#6b3a5d'>Total</td>
                            <td style='padding:10px;font-weight:bold;text-align:right;color:#6b3a5d'>{pago.Importe:N2} &euro;</td>
                        </tr>
                    </table>"
                    : $"<p style='font-size:24px;font-weight:bold;color:#6b3a5d;text-align:center;margin:20px 0'>{pago.Importe:N2} &euro;</p>";

                string html = $@"<!DOCTYPE html>
<html>
<head><meta charset='utf-8'></head>
<body style='margin:0;padding:0;background-color:#f8f4f6;font-family:-apple-system,BlinkMacSystemFont,""Segoe UI"",Roboto,Arial,sans-serif'>
    <table width='100%' cellpadding='0' cellspacing='0' style='background-color:#f8f4f6;padding:20px 0'>
        <tr><td align='center'>
            <table width='600' cellpadding='0' cellspacing='0' style='background:white;border-radius:12px;overflow:hidden;box-shadow:0 2px 12px rgba(0,0,0,0.08)'>
                <!-- Cabecera con logo -->
                <tr>
                    <td style='background:linear-gradient(135deg,#8b5a6b 0%,#6b3a5d 100%);padding:30px;text-align:center'>
                        <img src='{URL_LOGO}' alt='Nueva Visi&oacute;n' style='max-width:180px;height:auto' />
                    </td>
                </tr>
                <!-- Contenido -->
                <tr>
                    <td style='padding:30px 35px'>
                        <h1 style='color:#6b3a5d;font-size:22px;margin:0 0 15px 0'>Enlace de pago</h1>
                        <p style='color:#555;font-size:15px;line-height:1.6;margin:0 0 5px 0'>
                            Estimado cliente,
                        </p>
                        <p style='color:#555;font-size:15px;line-height:1.6;margin:0 0 20px 0'>
                            Le hemos preparado un enlace de pago seguro para que pueda realizar su abono de forma r&aacute;pida y c&oacute;moda.
                        </p>
                        {seccionEfectos}
                        <p style='text-align:center;margin:25px 0'>
                            <a href='{urlPaginaPago}' style='display:inline-block;background:linear-gradient(135deg,#8b5a6b 0%,#6b3a5d 100%);color:white;padding:14px 40px;text-decoration:none;border-radius:8px;font-size:16px;font-weight:bold;letter-spacing:0.5px'>
                                Realizar pago seguro
                            </a>
                        </p>
                        <p style='color:#999;font-size:12px;text-align:center;margin:15px 0 0 0'>
                            El pago se realiza a trav&eacute;s de la pasarela segura Redsys, con la m&aacute;xima protecci&oacute;n para sus datos.
                        </p>
                    </td>
                </tr>
                <!-- Pie -->
                <tr>
                    <td style='background:#f8f4f6;padding:20px 35px;border-top:1px solid #f0e8ec'>
                        <p style='color:#999;font-size:12px;margin:0;text-align:center'>
                            &iquest;Tiene alguna duda? Contacte con nosotros en
                            <a href='mailto:administracion@nuevavision.es' style='color:#8b5a6b'>administracion@nuevavision.es</a>
                        </p>
                        <p style='color:#ccc;font-size:11px;margin:8px 0 0 0;text-align:center'>
                            Nueva Visi&oacute;n &middot; Distribuci&oacute;n de productos de est&eacute;tica y peluquer&iacute;a profesional
                        </p>
                    </td>
                </tr>
            </table>
        </td></tr>
    </table>
</body>
</html>";

                using (var mail = new MailMessage())
                {
                    mail.From = new MailAddress(Correos.CORREO_ADMON, "Nueva Visión");
                    mail.To.Add(pago.Correo);
                    mail.Subject = $"Enlace de pago - {pago.Descripcion ?? "Nueva Visión"}";
                    mail.Body = html;
                    mail.IsBodyHtml = true;
                    _servicioCorreo.EnviarCorreoSMTP(mail);
                }
            }
            catch (Exception ex)
            {
                _logService.LogError($"[EnviarCorreoPreCobro] Error enviando correo a {pago.Correo}: {ex.Message}");
            }
        }

        /// <summary>
        /// Envía correo a administración con los detalles del cobro realizado.
        /// Issue #139: Correo post-cobro.
        /// </summary>
        internal void EnviarCorreoPostCobro(PagoTPV pago, string errorContabilizacion = null)
        {
            try
            {
                string filasEfectos = "";
                if (pago.PagosTPV_Efectos != null && pago.PagosTPV_Efectos.Any())
                {
                    bool alternar = false;
                    foreach (var e in pago.PagosTPV_Efectos)
                    {
                        string bgColor = alternar ? "background-color:#f9f9f9;" : "";
                        filasEfectos +=
                            $"<tr style='{bgColor}'>" +
                            $"<td style='padding:8px;border-bottom:1px solid #eee'>{e.Documento?.Trim()}</td>" +
                            $"<td style='padding:8px;border-bottom:1px solid #eee'>{e.Efecto?.Trim()}</td>" +
                            $"<td style='padding:8px;border-bottom:1px solid #eee'>{e.Contacto?.Trim()}</td>" +
                            $"<td style='padding:8px;border-bottom:1px solid #eee;text-align:right'>{e.Importe:N2} &euro;</td></tr>";
                        alternar = !alternar;
                    }
                }

                // Issue #143: Alerta de error de contabilización
                string seccionError = !string.IsNullOrEmpty(errorContabilizacion)
                    ? $@"<div style='background:#fdecea;border:1px solid #f5c6cb;border-radius:6px;padding:15px;margin:0 0 15px 0'>
                        <strong style='color:#c0392b'>ERROR: No se ha podido contabilizar el cobro</strong>
                        <p style='color:#721c24;margin:8px 0 0 0;font-size:13px'>{HttpUtility.HtmlEncode(errorContabilizacion)}</p>
                    </div>"
                    : "";

                string seccionEfectos = pago.PagosTPV_Efectos != null && pago.PagosTPV_Efectos.Any()
                    ? $@"<h3 style='color:#333;font-size:14px;margin:20px 0 10px 0'>Efectos cobrados</h3>
                    <table style='border-collapse:collapse;width:100%'>
                        <tr style='background:#f5f5f5'>
                            <th style='padding:8px;text-align:left;border-bottom:2px solid #ddd;font-size:12px'>Documento</th>
                            <th style='padding:8px;text-align:left;border-bottom:2px solid #ddd;font-size:12px'>Efecto</th>
                            <th style='padding:8px;text-align:left;border-bottom:2px solid #ddd;font-size:12px'>Contacto</th>
                            <th style='padding:8px;text-align:right;border-bottom:2px solid #ddd;font-size:12px'>Importe</th>
                        </tr>
                        {filasEfectos}
                    </table>"
                    : "";

                string html = $@"<!DOCTYPE html>
<html>
<head><meta charset='utf-8'></head>
<body style='margin:0;padding:0;background-color:#f4f4f4;font-family:-apple-system,BlinkMacSystemFont,""Segoe UI"",Roboto,Arial,sans-serif'>
    <table width='100%' cellpadding='0' cellspacing='0' style='background-color:#f4f4f4;padding:20px 0'>
        <tr><td align='center'>
            <table width='600' cellpadding='0' cellspacing='0' style='background:white;border-radius:8px;overflow:hidden;box-shadow:0 2px 8px rgba(0,0,0,0.06)'>
                <!-- Cabecera -->
                <tr>
                    <td style='background:#27ae60;padding:20px 30px;text-align:center'>
                        <img src='{URL_LOGO}' alt='Nueva Visi&oacute;n' style='max-width:120px;height:auto;margin-bottom:8px' />
                        <h1 style='color:white;font-size:18px;margin:0'>Cobro NestoPago realizado</h1>
                    </td>
                </tr>
                <!-- Datos del cobro -->
                <tr>
                    <td style='padding:25px 30px'>
                        {seccionError}
                        <table style='width:100%;font-size:14px'>
                            <tr><td style='padding:6px 0;color:#888;width:140px'>Cliente</td><td style='padding:6px 0;font-weight:bold'>{pago.Cliente?.Trim()}</td></tr>
                            <tr><td style='padding:6px 0;color:#888'>Importe</td><td style='padding:6px 0;font-weight:bold;color:#27ae60;font-size:18px'>{pago.Importe:N2} &euro;</td></tr>
                            <tr><td style='padding:6px 0;color:#888'>N&ordm; Orden</td><td style='padding:6px 0'>{pago.NumeroOrden}</td></tr>
                            <tr><td style='padding:6px 0;color:#888'>Autorizaci&oacute;n</td><td style='padding:6px 0'>{pago.CodigoAutorizacion}</td></tr>
                            <tr><td style='padding:6px 0;color:#888'>Fecha</td><td style='padding:6px 0'>{pago.FechaActualizacion:g}</td></tr>
                            <tr><td style='padding:6px 0;color:#888'>Correo cliente</td><td style='padding:6px 0'>{pago.Correo}</td></tr>
                            <tr><td style='padding:6px 0;color:#888'>Usuario</td><td style='padding:6px 0'>{pago.Usuario}</td></tr>
                        </table>
                        {seccionEfectos}
                    </td>
                </tr>
            </table>
        </td></tr>
    </table>
</body>
</html>";

                using (var mail = new MailMessage())
                {
                    mail.From = new MailAddress(Correos.CORREO_ADMON, "NestoPago");
                    mail.To.Add(Correos.CORREO_ADMON);

                    // Issue #142: CC al creador del enlace de pago
                    string correoCreador = ObtenerCorreoUsuario(pago.Usuario);
                    if (!string.IsNullOrEmpty(correoCreador))
                    {
                        try
                        {
                            mail.CC.Add(correoCreador);
                        }
                        catch
                        {
                            // Si el correo no es válido, ignorar
                        }
                    }

                    string prefijoAsunto = !string.IsNullOrEmpty(errorContabilizacion) ? "ERROR " : "";
                    mail.Subject = $"{prefijoAsunto}Cobro NestoPago: {pago.Importe:C} - Cliente {pago.Cliente?.Trim()}";
                    mail.Body = html;
                    mail.IsBodyHtml = true;
                    _servicioCorreo.EnviarCorreoSMTP(mail);
                }
            }
            catch (Exception ex)
            {
                _logService.LogError($"[EnviarCorreoPostCobro] Error enviando correo post-cobro: {ex.Message}");
            }
        }

        /// <summary>
        /// Obtiene el correo electrónico de un usuario.
        /// Si el usuario ya es un email, lo devuelve directamente.
        /// Si es un usuario de Windows (DOMINIO\Usuario), lee el parámetro Parametros.Claves.CorreoDefecto.
        /// </summary>
        internal string ObtenerCorreoUsuario(string usuario)
        {
            if (string.IsNullOrWhiteSpace(usuario))
            {
                return null;
            }

            // Si ya parece un email, devolverlo directamente
            if (usuario.Contains("@"))
            {
                return usuario.Trim();
            }

            // Extraer nombre de usuario sin dominio (NUEVAVISION\Lidia → Lidia)
            string nombreUsuario = usuario.Contains("\\")
                ? usuario.Substring(usuario.IndexOf('\\') + 1)
                : usuario;

            try
            {
                return _lectorParametros.LeerParametro(
                    Empresas.EMPRESA_POR_DEFECTO, nombreUsuario, Parametros.Claves.CorreoDefecto);
            }
            catch
            {
                return null;
            }
        }

        internal void EnviarCorreoAlertaPago(string titulo, string detalle, ResultadoValidacionNotificacion resultado)
        {
            try
            {
                string html = $@"<!DOCTYPE html>
<html>
<head><meta charset='utf-8'></head>
<body style='margin:0;padding:0;background-color:#f4f4f4;font-family:-apple-system,BlinkMacSystemFont,""Segoe UI"",Roboto,Arial,sans-serif'>
    <table width='100%' cellpadding='0' cellspacing='0' style='background-color:#f4f4f4;padding:20px 0'>
        <tr><td align='center'>
            <table width='600' cellpadding='0' cellspacing='0' style='background:white;border-radius:8px;overflow:hidden;box-shadow:0 2px 8px rgba(0,0,0,0.06)'>
                <tr>
                    <td style='background:#c0392b;padding:20px 30px;text-align:center'>
                        <img src='{URL_LOGO}' alt='Nueva Visi&oacute;n' style='max-width:120px;height:auto;margin-bottom:8px' />
                        <h1 style='color:white;font-size:18px;margin:0'>ALERTA NestoPago</h1>
                    </td>
                </tr>
                <tr>
                    <td style='padding:25px 30px'>
                        <div style='background:#fdecea;border:1px solid #f5c6cb;border-radius:6px;padding:15px;margin:0 0 15px 0'>
                            <strong style='color:#c0392b'>{HttpUtility.HtmlEncode(titulo)}</strong>
                        </div>
                        <table style='width:100%;font-size:14px'>
                            <tr><td style='padding:6px 0;color:#888;width:160px'>N&ordm; Orden Redsys</td><td style='padding:6px 0;font-weight:bold'>{resultado?.NumeroOrden}</td></tr>
                            <tr><td style='padding:6px 0;color:#888'>Pago autorizado</td><td style='padding:6px 0;font-weight:bold'>{(resultado?.PagoAutorizado == true ? "SI" : "NO")}</td></tr>
                            <tr><td style='padding:6px 0;color:#888'>C&oacute;digo respuesta</td><td style='padding:6px 0'>{resultado?.CodigoRespuesta}</td></tr>
                            <tr><td style='padding:6px 0;color:#888'>C&oacute;digo autorizaci&oacute;n</td><td style='padding:6px 0'>{resultado?.CodigoAutorizacion}</td></tr>
                        </table>
                        <p style='color:#555;font-size:13px;margin:15px 0 0 0;padding:10px;background:#f9f9f9;border-radius:4px;word-break:break-all'>
                            {HttpUtility.HtmlEncode(detalle)}
                        </p>
                        <p style='color:#999;font-size:12px;margin:15px 0 0 0'>
                            Este correo se ha generado autom&aacute;ticamente porque se ha recibido una notificaci&oacute;n de Redsys
                            que no se ha podido procesar correctamente. Es necesario investigar y actuar manualmente.
                        </p>
                    </td>
                </tr>
            </table>
        </td></tr>
    </table>
</body>
</html>";

                using (var mail = new MailMessage())
                {
                    mail.From = new MailAddress(Correos.CORREO_ADMON, "NestoPago");
                    mail.To.Add(Correos.CORREO_ADMON);
                    mail.Subject = $"ALERTA NestoPago: {titulo} - Orden {resultado?.NumeroOrden}";
                    mail.Body = html;
                    mail.IsBodyHtml = true;
                    _servicioCorreo.EnviarCorreoSMTP(mail);
                }
            }
            catch (Exception ex)
            {
                _logService.LogError($"[EnviarCorreoAlertaPago] Error enviando correo de alerta: {ex.Message}. Alerta original: {detalle}");
            }
        }

        internal const int LIMITE_REINTENTOS_PAGO = 3;

        internal async Task RegenerarPagoDenegado(PagoTPV pagoDenegado, NVEntities db)
        {
            // Buscar el pago raíz de la cadena de reintentos
            int pagoRaizId = pagoDenegado.PagoOriginalId ?? pagoDenegado.Id;

            // Contar reintentos previos vinculados al pago raíz
            int reintentosPrevios = await db.PagosTPV
                .CountAsync(p => p.PagoOriginalId == pagoRaizId)
                .ConfigureAwait(false);

            if (reintentosPrevios >= LIMITE_REINTENTOS_PAGO)
            {
                EnviarCorreoLimiteReintentos(pagoDenegado);
                return;
            }

            // Crear nuevos parámetros Redsys
            string urlBase = "https://api.nuevavision.es";
            string urlNotificacion = urlBase + "/api/Pagos/NotificacionRedsys";
            string urlOk = urlBase + "/pago/ok.html";
            string urlKo = urlBase + "/pago/ko.html";

            ParametrosRedsysFirmados parametros = _redsysService.CrearParametrosTPVVirtual(
                pagoDenegado.Importe,
                pagoDenegado.Descripcion,
                pagoDenegado.Correo,
                pagoDenegado.Cliente,
                urlNotificacion,
                urlOk,
                urlKo);

            // Crear nuevo PagoTPV con los mismos datos
            var nuevoPago = new PagoTPV
            {
                NumeroOrden = parametros.NumeroOrden,
                Tipo = pagoDenegado.Tipo,
                Empresa = pagoDenegado.Empresa,
                Cliente = pagoDenegado.Cliente,
                Contacto = pagoDenegado.Contacto,
                Importe = pagoDenegado.Importe,
                Descripcion = pagoDenegado.Descripcion,
                Correo = pagoDenegado.Correo,
                Movil = pagoDenegado.Movil,
                ExtractoClienteId = pagoDenegado.ExtractoClienteId,
                Documento = pagoDenegado.Documento,
                Efecto = pagoDenegado.Efecto,
                Vendedor = pagoDenegado.Vendedor,
                FormaVenta = pagoDenegado.FormaVenta,
                Delegacion = pagoDenegado.Delegacion,
                TipoApunte = pagoDenegado.TipoApunte,
                Estado = Constantes.EstadosPagoTPV.PENDIENTE,
                FechaCreacion = DateTime.Now,
                Usuario = pagoDenegado.Usuario,
                TokenAcceso = Guid.NewGuid(),
                PagoOriginalId = pagoRaizId
            };

            db.PagosTPV.Add(nuevoPago);
            await db.SaveChangesAsync().ConfigureAwait(false);

            // Duplicar efectos
            if (pagoDenegado.PagosTPV_Efectos != null)
            {
                foreach (var efecto in pagoDenegado.PagosTPV_Efectos)
                {
                    db.PagosTPV_Efectos.Add(new PagoTPV_Efecto
                    {
                        IdPago = nuevoPago.Id,
                        ExtractoClienteId = efecto.ExtractoClienteId,
                        Importe = efecto.Importe,
                        Documento = efecto.Documento,
                        Efecto = efecto.Efecto,
                        Contacto = efecto.Contacto,
                        Vendedor = efecto.Vendedor,
                        FormaVenta = efecto.FormaVenta,
                        Delegacion = efecto.Delegacion,
                        TipoApunte = efecto.TipoApunte
                    });
                }
                await db.SaveChangesAsync().ConfigureAwait(false);
            }

            string urlPaginaPago = $"https://api.nuevavision.es/pago/{nuevoPago.TokenAcceso}";
            EnviarCorreoPagoDenegado(pagoDenegado, urlPaginaPago);
        }

        internal void EnviarCorreoPagoDenegado(PagoTPV pagoDenegado, string urlNuevoPago)
        {
            try
            {
                string filasEfectos = "";
                if (pagoDenegado.PagosTPV_Efectos != null && pagoDenegado.PagosTPV_Efectos.Any())
                {
                    bool alternar = false;
                    foreach (var e in pagoDenegado.PagosTPV_Efectos)
                    {
                        string bgColor = alternar ? "background-color:#fef5f5;" : "";
                        filasEfectos +=
                            $"<tr style='{bgColor}'>" +
                            $"<td style='padding:10px;border-bottom:1px solid #f0e0e0'>{e.Documento?.Trim()}</td>" +
                            $"<td style='padding:10px;border-bottom:1px solid #f0e0e0;text-align:right;white-space:nowrap'>{e.Importe:N2} &euro;</td></tr>";
                        alternar = !alternar;
                    }
                }

                string seccionEfectos = pagoDenegado.PagosTPV_Efectos != null && pagoDenegado.PagosTPV_Efectos.Any()
                    ? $@"<table style='border-collapse:collapse;width:100%;margin:20px 0'>
                        <tr style='background:#fef5f5'>
                            <th style='padding:10px;text-align:left;border-bottom:2px solid #e8b4b4;color:#8b3a3a'>Documento</th>
                            <th style='padding:10px;text-align:right;border-bottom:2px solid #e8b4b4;color:#8b3a3a'>Importe</th>
                        </tr>
                        {filasEfectos}
                        <tr style='background:#fef5f5'>
                            <td style='padding:10px;font-weight:bold;color:#8b3a3a'>Total</td>
                            <td style='padding:10px;font-weight:bold;text-align:right;color:#8b3a3a'>{pagoDenegado.Importe:N2} &euro;</td>
                        </tr>
                    </table>"
                    : $"<p style='font-size:24px;font-weight:bold;color:#8b3a3a;text-align:center;margin:20px 0'>{pagoDenegado.Importe:N2} &euro;</p>";

                string html = $@"<!DOCTYPE html>
<html>
<head><meta charset='utf-8'></head>
<body style='margin:0;padding:0;background-color:#fef5f5;font-family:-apple-system,BlinkMacSystemFont,""Segoe UI"",Roboto,Arial,sans-serif'>
    <table width='100%' cellpadding='0' cellspacing='0' style='background-color:#fef5f5;padding:20px 0'>
        <tr><td align='center'>
            <table width='600' cellpadding='0' cellspacing='0' style='background:white;border-radius:12px;overflow:hidden;box-shadow:0 2px 12px rgba(0,0,0,0.08)'>
                <!-- Cabecera con logo -->
                <tr>
                    <td style='background:linear-gradient(135deg,#c0392b 0%,#8b3a3a 100%);padding:30px;text-align:center'>
                        <img src='{URL_LOGO}' alt='Nueva Visi&oacute;n' style='max-width:180px;height:auto' />
                    </td>
                </tr>
                <!-- Contenido -->
                <tr>
                    <td style='padding:30px 35px'>
                        <h1 style='color:#8b3a3a;font-size:22px;margin:0 0 15px 0'>Pago no procesado</h1>
                        <p style='color:#555;font-size:15px;line-height:1.6;margin:0 0 5px 0'>
                            Estimado cliente,
                        </p>
                        <p style='color:#555;font-size:15px;line-height:1.6;margin:0 0 20px 0'>
                            Le informamos de que su intento de pago no ha podido ser procesado. No se ha realizado ning&uacute;n cargo en su tarjeta.
                        </p>
                        <p style='color:#555;font-size:15px;line-height:1.6;margin:0 0 20px 0'>
                            Hemos generado autom&aacute;ticamente un nuevo enlace de pago para que pueda reintentar la operaci&oacute;n cuando lo desee.
                        </p>
                        {seccionEfectos}
                        <p style='text-align:center;margin:25px 0'>
                            <a href='{urlNuevoPago}' style='display:inline-block;background:linear-gradient(135deg,#8b5a6b 0%,#6b3a5d 100%);color:white;padding:14px 40px;text-decoration:none;border-radius:8px;font-size:16px;font-weight:bold;letter-spacing:0.5px'>
                                Reintentar pago seguro
                            </a>
                        </p>
                        <p style='color:#999;font-size:12px;text-align:center;margin:15px 0 0 0'>
                            El pago se realiza a trav&eacute;s de la pasarela segura Redsys, con la m&aacute;xima protecci&oacute;n para sus datos.
                        </p>
                    </td>
                </tr>
                <!-- Pie -->
                <tr>
                    <td style='background:#fef5f5;padding:20px 35px;border-top:1px solid #f0e0e0'>
                        <p style='color:#999;font-size:12px;margin:0;text-align:center'>
                            &iquest;Tiene alguna duda? Contacte con nosotros en
                            <a href='mailto:administracion@nuevavision.es' style='color:#8b5a6b'>administracion@nuevavision.es</a>
                        </p>
                        <p style='color:#ccc;font-size:11px;margin:8px 0 0 0;text-align:center'>
                            Nueva Visi&oacute;n &middot; Distribuci&oacute;n de productos de est&eacute;tica y peluquer&iacute;a profesional
                        </p>
                    </td>
                </tr>
            </table>
        </td></tr>
    </table>
</body>
</html>";

                // Enviar al cliente
                if (!string.IsNullOrWhiteSpace(pagoDenegado.Correo))
                {
                    using (var mail = new MailMessage())
                    {
                        mail.From = new MailAddress(Correos.CORREO_ADMON, "Nueva Visión");
                        mail.To.Add(pagoDenegado.Correo);
                        mail.CC.Add(Correos.CORREO_ADMON);
                        mail.Subject = $"Pago no procesado - {pagoDenegado.Descripcion ?? "Nueva Visión"} - Nuevo enlace disponible";
                        mail.Body = html;
                        mail.IsBodyHtml = true;
                        _servicioCorreo.EnviarCorreoSMTP(mail);
                    }
                }
                else
                {
                    // Sin correo de cliente, avisar solo a administración
                    EnviarCorreoLimiteReintentos(pagoDenegado);
                }
            }
            catch (Exception ex)
            {
                _logService.LogError($"[EnviarCorreoPagoDenegado] Error enviando correo: {ex.Message}");
            }
        }

        internal void EnviarCorreoLimiteReintentos(PagoTPV pago)
        {
            try
            {
                string html = $@"<!DOCTYPE html>
<html>
<head><meta charset='utf-8'></head>
<body style='margin:0;padding:0;background-color:#f4f4f4;font-family:-apple-system,BlinkMacSystemFont,""Segoe UI"",Roboto,Arial,sans-serif'>
    <table width='100%' cellpadding='0' cellspacing='0' style='background-color:#f4f4f4;padding:20px 0'>
        <tr><td align='center'>
            <table width='600' cellpadding='0' cellspacing='0' style='background:white;border-radius:8px;overflow:hidden;box-shadow:0 2px 8px rgba(0,0,0,0.06)'>
                <tr>
                    <td style='background:#c0392b;padding:20px 30px;text-align:center'>
                        <img src='{URL_LOGO}' alt='Nueva Visi&oacute;n' style='max-width:120px;height:auto;margin-bottom:8px' />
                        <h1 style='color:white;font-size:18px;margin:0'>L&iacute;mite de reintentos NestoPago</h1>
                    </td>
                </tr>
                <tr>
                    <td style='padding:25px 30px'>
                        <div style='background:#fdecea;border:1px solid #f5c6cb;border-radius:6px;padding:15px;margin:0 0 15px 0'>
                            <strong style='color:#c0392b'>Se ha superado el l&iacute;mite de {LIMITE_REINTENTOS_PAGO} reintentos autom&aacute;ticos</strong>
                            <p style='color:#721c24;margin:8px 0 0 0;font-size:13px'>
                                El cliente ha agotado los intentos autom&aacute;ticos de pago. Es necesario intervenci&oacute;n manual para generar un nuevo enlace.
                            </p>
                        </div>
                        <table style='width:100%;font-size:14px'>
                            <tr><td style='padding:6px 0;color:#888;width:140px'>Cliente</td><td style='padding:6px 0;font-weight:bold'>{pago.Cliente?.Trim()}</td></tr>
                            <tr><td style='padding:6px 0;color:#888'>Importe</td><td style='padding:6px 0;font-weight:bold;color:#c0392b;font-size:18px'>{pago.Importe:N2} &euro;</td></tr>
                            <tr><td style='padding:6px 0;color:#888'>N&ordm; Orden</td><td style='padding:6px 0'>{pago.NumeroOrden}</td></tr>
                            <tr><td style='padding:6px 0;color:#888'>C&oacute;digo respuesta</td><td style='padding:6px 0'>{pago.CodigoRespuesta}</td></tr>
                            <tr><td style='padding:6px 0;color:#888'>Correo cliente</td><td style='padding:6px 0'>{pago.Correo}</td></tr>
                            <tr><td style='padding:6px 0;color:#888'>Usuario</td><td style='padding:6px 0'>{pago.Usuario}</td></tr>
                        </table>
                    </td>
                </tr>
            </table>
        </td></tr>
    </table>
</body>
</html>";

                using (var mail = new MailMessage())
                {
                    mail.From = new MailAddress(Correos.CORREO_ADMON, "NestoPago");
                    mail.To.Add(Correos.CORREO_ADMON);
                    mail.Subject = $"LIMITE REINTENTOS NestoPago: {pago.Importe:C} - Cliente {pago.Cliente?.Trim()}";
                    mail.Body = html;
                    mail.IsBodyHtml = true;
                    _servicioCorreo.EnviarCorreoSMTP(mail);
                }
            }
            catch (Exception ex)
            {
                _logService.LogError($"[EnviarCorreoLimiteReintentos] Error enviando correo: {ex.Message}");
            }
        }

        private static string ObtenerMensajeCompletoExcepcion(Exception ex)
        {
            var mensajes = new System.Text.StringBuilder();
            var actual = ex;
            while (actual != null)
            {
                if (mensajes.Length > 0) mensajes.Append(" → ");
                mensajes.Append(actual.Message);
                actual = actual.InnerException;
            }
            return mensajes.ToString();
        }
    }
}
