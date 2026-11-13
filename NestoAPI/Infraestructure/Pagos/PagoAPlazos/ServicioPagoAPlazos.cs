using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Pagos.PagoAPlazos
{
    /// <summary>NestoAPI#864: lo que contesta el núcleo; el controlador lo pasa tal cual (código HTTP, mensaje de usted, contenido).</summary>
    public class ResultadoPagoAPlazos<T>
    {
        public HttpStatusCode Codigo { get; private set; }
        public string Mensaje { get; private set; }
        public T Contenido { get; private set; }
        public bool EsOk => Codigo == HttpStatusCode.OK;

        public static ResultadoPagoAPlazos<T> Bien(T contenido) => new ResultadoPagoAPlazos<T> { Codigo = HttpStatusCode.OK, Contenido = contenido };

        public static ResultadoPagoAPlazos<T> Mal(HttpStatusCode codigo, string mensaje)
            => new ResultadoPagoAPlazos<T> { Codigo = codigo, Mensaje = mensaje };
    }

    /// <summary>Lo que ha hecho una pasada del job (para el log y los tests).</summary>
    public class ResultadoPlazosPagoAPlazos
    {
        public List<int> Capturadas { get; } = new List<int>();
        public List<int> Avisadas { get; } = new List<int>();
        public List<int> Caducadas { get; } = new List<int>();
        public List<int> Canceladas { get; } = new List<int>();
        public List<int> Fallidas { get; } = new List<int>();
    }

    public interface IServicioPagoAPlazos
    {
        Task<ResultadoPagoAPlazos<DisponibilidadPagoAPlazosDTO>> Disponible(string empresa, int pedido);
        Task<ResultadoPagoAPlazos<SolicitudPagoAPlazosDTO>> Solicitar(string empresa, int pedido, PeticionPagoAPlazosDTO peticion, string usuario);
        Task<ResultadoPagoAPlazos<EstadoPagoAPlazosDTO>> Estado(string empresa, int pedido);
        Task<ResultadoPagoAPlazos<EstadoPagoAPlazosDTO>> Cancelar(string empresa, int pedido, string motivo, string usuario);

        /// <summary>El cliente del pedido (para los permisos), o null si el pedido no existe.</summary>
        Task<string> ClienteDelPedido(string empresa, int pedido);

        /// <summary>La solicitud del pedido para la app de clientes: solo si el pedido es de ese cliente.</summary>
        Task<ResultadoPagoAPlazos<PagoAPlazosClienteDTO>> ParaElCliente(string empresa, string cliente, int pedido);

        Task<RespuestaNotificacionProveedor> ProcesarNotificacion(string codigoProveedor, string cabeceraAutorizacion, string cuerpo);

        /// <summary>El pedido se ha borrado: se cancelan sus solicitudes vivas. Nunca lanza.</summary>
        Task CancelarPorPedidoBorrado(string empresa, int pedido, string usuario);

        /// <summary>El job: capturas, avisos de plazo, caducidades y cancelaciones.</summary>
        Task<ResultadoPlazosPagoAPlazos> ProcesarPlazos(DateTime ahora);
    }

    /// <summary>
    /// NestoAPI#864: el NÚCLEO del pago a plazos de un pedido que ya existe (lo pasa un empleado en Nesto o un vendedor en
    /// NestoApp y lo termina el cliente, en la app o con un enlace). Lleva el pedido, el prepago con su fecha de cobro, los
    /// estados, el canal, los avisos y los plazos, y NO conoce a ninguna financiera: solo habla con
    /// <see cref="IProveedorPagoAPlazos"/>, que sale del <see cref="IRegistroProveedoresPagoAPlazos"/> por el código que dice
    /// el parámetro (para las solicitudes nuevas) o la propia solicitud (para las que ya existen).
    ///
    /// <para><b>Lo que nunca puede pasar</b>: que un pedido tenga el prepago sin que el proveedor haya aprobado ese importe
    /// exacto. La solicitud solo pasa a Aprobado con un UPDATE condicional desde Pendiente, cuando el proveedor pide la
    /// confirmación con el importe de la solicitud, y el prepago es idempotente por su concepto (lleva la referencia).</para>
    /// </summary>
    public class ServicioPagoAPlazos : IServicioPagoAPlazos
    {
        public const string MENSAJE_SIN_SCRIPT =
            "El pago a plazos de los pedidos todavía no está disponible: falta lanzar Scripts/Issue864_PagoAPlazos.sql en la base de datos.";

        internal const string RUTA_NOTIFICACION = "/api/PagoAPlazos/Notificacion/";

        private readonly IRepositorioPagoAPlazos repositorio;
        private readonly IPedidosPagoAPlazos pedidos;
        private readonly IRegistroProveedoresPagoAPlazos registro;
        private readonly Func<ConfiguracionPagoAPlazos> leerConfiguracion;
        private readonly IAvisosPagoAPlazos avisos;

        public ServicioPagoAPlazos(IRepositorioPagoAPlazos repositorio, IPedidosPagoAPlazos pedidos, IRegistroProveedoresPagoAPlazos registro,
            Func<ConfiguracionPagoAPlazos> leerConfiguracion, IAvisosPagoAPlazos avisos)
        {
            this.repositorio = repositorio;
            this.pedidos = pedidos;
            this.registro = registro;
            this.leerConfiguracion = leerConfiguracion;
            this.avisos = avisos;
        }

        public static IServicioPagoAPlazos CrearPorDefecto()
        {
            return new ServicioPagoAPlazos(new RepositorioPagoAPlazos(), new PedidosPagoAPlazos(), RegistroProveedoresPagoAPlazos.PorDefecto(),
                () => ConfiguracionPagoAPlazos.Leer(new LectorParametrosUsuario()), new AvisosPagoAPlazos());
        }

        /// <summary>Errores a ELMAH. Sustituible en tests.</summary>
        internal Action<Exception> RegistrarError { get; set; } = ElmahHelper.Log;

        internal Func<DateTime> Ahora { get; set; } = () => DateTime.Now;

        internal static string UrlNotificacion(string codigoProveedor)
            => ServicioPagos.URL_BASE_API + RUTA_NOTIFICACION + Uri.EscapeDataString(codigoProveedor?.Trim() ?? string.Empty);

        private static string EmpresaDe(string empresa)
            => string.IsNullOrWhiteSpace(empresa) ? Models.Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();

        #region Lo que se le ofrece a quien pasa el pedido

        private class Contexto
        {
            public PedidoPagoAPlazos Pedido;
            public ConfiguracionPagoAPlazos Configuracion;
            public IProveedorPagoAPlazos Proveedor;
            public CondicionesProveedorPagoAPlazos Condiciones;
            public List<SolicitudPagoAPlazos> Solicitudes;
            public string Motivo;
        }

        private async Task<Contexto> LeerContexto(string empresa, int pedido)
        {
            PedidoPagoAPlazos datos = await pedidos.Leer(empresa, pedido).ConfigureAwait(false);
            if (datos == null)
            {
                return null;
            }
            Contexto contexto = new Contexto
            {
                Pedido = datos,
                Configuracion = leerConfiguracion() ?? new ConfiguracionPagoAPlazos(),
                Solicitudes = await repositorio.LeerDelPedido(empresa, pedido).ConfigureAwait(false) ?? new List<SolicitudPagoAPlazos>()
            };
            contexto.Proveedor = registro.PorCodigo(contexto.Configuracion.Proveedor);
            if (contexto.Proveedor == null && contexto.Configuracion.Modo != ModoPagoAPlazos.Apagado)
            {
                RegistrarError(new Exception($"[Pago a plazos #864] El interruptor está encendido pero el proveedor «{contexto.Configuracion.Proveedor}» " +
                    $"(parámetro {ConfiguracionPagoAPlazos.CLAVE_PROVEEDOR}) no está dado de alta: no se ofrece."));
            }
            try
            {
                contexto.Condiciones = contexto.Proveedor?.Condiciones();
            }
            catch (Exception ex)
            {
                RegistrarError(new Exception($"[Pago a plazos #864] No se han podido leer las condiciones de {contexto.Proveedor?.Nombre}: {ex.Message}", ex));
            }
            contexto.Motivo = ReglasPagoAPlazos.MotivoNoDisponible(contexto.Configuracion, contexto.Proveedor?.Nombre, contexto.Condiciones,
                datos, contexto.Solicitudes, Ahora());
            return contexto;
        }

        public async Task<ResultadoPagoAPlazos<DisponibilidadPagoAPlazosDTO>> Disponible(string empresa, int pedido)
        {
            if (!repositorio.TablaDisponible())
            {
                return ResultadoPagoAPlazos<DisponibilidadPagoAPlazosDTO>.Mal(HttpStatusCode.ServiceUnavailable, MENSAJE_SIN_SCRIPT);
            }
            empresa = EmpresaDe(empresa);
            Contexto contexto = await LeerContexto(empresa, pedido).ConfigureAwait(false);
            if (contexto == null)
            {
                return ResultadoPagoAPlazos<DisponibilidadPagoAPlazosDTO>.Mal(HttpStatusCode.NotFound, $"No existe el pedido {pedido}.");
            }
            bool seOfrece = contexto.Configuracion.SeOfreceA(contexto.Pedido.Cliente) && contexto.Proveedor != null && contexto.Condiciones != null
                && contexto.Condiciones.Operativo;
            string canal = contexto.Pedido.ClienteTieneApp ? CanalesPagoAPlazos.APP : CanalesPagoAPlazos.ENLACE;
            return ResultadoPagoAPlazos<DisponibilidadPagoAPlazosDTO>.Bien(new DisponibilidadPagoAPlazosDTO
            {
                Disponible = contexto.Motivo == null,
                Motivo = contexto.Motivo,
                // Con el interruptor apagado (o sin proveedor) no se dice ni cuál sería
                Proveedor = seOfrece ? contexto.Proveedor.Codigo : null,
                NombreProveedor = seOfrece ? contexto.Proveedor.Nombre : null,
                Importe = ReglasPagoAPlazos.ImportePendiente(contexto.Pedido),
                ImporteMinimo = seOfrece ? contexto.Condiciones.ImporteMinimo : null,
                ImporteMaximo = seOfrece ? contexto.Condiciones.ImporteMaximo : null,
                DiasPlazoCaptura = seOfrece ? contexto.Condiciones.DiasPlazoCaptura : (int?)null,
                ClienteTieneApp = contexto.Pedido.ClienteTieneApp,
                Canal = canal,
                Aviso = contexto.Motivo != null ? null : Aviso(contexto.Pedido, canal),
                Pruebas = seOfrece && contexto.Configuracion.Pruebas
            });
        }

        private static string Aviso(PedidoPagoAPlazos pedido, string canal)
        {
            List<string> avisos = new List<string>();
            if (canal == CanalesPagoAPlazos.ENLACE && !pedido.ClienteTieneApp)
            {
                avisos.Add(ReglasPagoAPlazos.AVISO_SIN_APP);
            }
            if (pedido.ContraReembolso)
            {
                avisos.Add(ReglasPagoAPlazos.AVISO_CONTRA_REEMBOLSO);
            }
            return avisos.Count == 0 ? null : string.Join(" ", avisos);
        }

        public async Task<string> ClienteDelPedido(string empresa, int pedido)
        {
            PedidoPagoAPlazos datos = await pedidos.Leer(EmpresaDe(empresa), pedido).ConfigureAwait(false);
            return datos == null ? null : datos.Cliente ?? string.Empty;
        }

        #endregion

        #region Abrir la solicitud

        public async Task<ResultadoPagoAPlazos<SolicitudPagoAPlazosDTO>> Solicitar(string empresa, int pedido, PeticionPagoAPlazosDTO peticion, string usuario)
        {
            ResultadoPagoAPlazos<SolicitudPagoAPlazosDTO> Mal(HttpStatusCode codigo, string mensaje)
                => ResultadoPagoAPlazos<SolicitudPagoAPlazosDTO>.Mal(codigo, mensaje);

            if (!repositorio.TablaDisponible())
            {
                return Mal(HttpStatusCode.ServiceUnavailable, MENSAJE_SIN_SCRIPT);
            }
            empresa = EmpresaDe(empresa);
            Contexto contexto = await LeerContexto(empresa, pedido).ConfigureAwait(false);
            if (contexto == null)
            {
                return Mal(HttpStatusCode.NotFound, $"No existe el pedido {pedido}.");
            }
            if (contexto.Motivo != null)
            {
                return Mal(HttpStatusCode.Conflict, contexto.Motivo);
            }
            string canal = ReglasPagoAPlazos.ElegirCanal(peticion?.Canal, contexto.Pedido.ClienteTieneApp, out string motivoCanal, out bool conflicto);
            if (canal == null)
            {
                return Mal(conflicto ? HttpStatusCode.Conflict : HttpStatusCode.BadRequest, motivoCanal);
            }
            string correo = string.IsNullOrWhiteSpace(peticion?.Correo) ? contexto.Pedido.Correo?.Trim() : peticion.Correo.Trim();
            if (string.IsNullOrWhiteSpace(correo) || !correo.Contains("@"))
            {
                return Mal(HttpStatusCode.BadRequest,
                    "Para el pago a plazos hace falta el correo electrónico del cliente: indíquelo o añádalo a su ficha.");
            }

            DateTime ahora = Ahora();
            IProveedorPagoAPlazos proveedor = contexto.Proveedor;
            decimal importe = ReglasPagoAPlazos.ImportePendiente(contexto.Pedido);
            string aviso = Aviso(contexto.Pedido, canal);

            // Si ya hay una pendiente igual (mismo proveedor, canal e importe), es la misma: se le vuelve a avisar al cliente.
            // Si es distinta (otro canal, el pedido ha cambiado), la anterior se cierra para que no se pueda aprobar.
            foreach (SolicitudPagoAPlazos anterior in contexto.Solicitudes.Where(s => EstadosPagoAPlazos.Es(s.Estado, EstadosPagoAPlazos.PENDIENTE)))
            {
                bool igual = !ReglasPagoAPlazos.PendienteCaducada(anterior, ahora, contexto.Configuracion.DiasValidezSolicitud)
                    && string.Equals(anterior.Proveedor?.Trim(), proveedor.Codigo, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(anterior.Canal?.Trim(), canal, StringComparison.OrdinalIgnoreCase)
                    && anterior.Importe == importe && anterior.Pruebas == contexto.Configuracion.Pruebas
                    && !string.IsNullOrWhiteSpace(anterior.ReferenciaExterna);
                if (igual)
                {
                    return ResultadoPagoAPlazos<SolicitudPagoAPlazosDTO>.Bien(await Entregar(anterior, proveedor, aviso).ConfigureAwait(false));
                }
                bool cerrada = await repositorio.CambiarEstado(anterior.Id, new[] { EstadosPagoAPlazos.PENDIENTE }, EstadosPagoAPlazos.CANCELADO, ahora,
                    "Sustituida por otra solicitud del mismo pedido").ConfigureAwait(false);
                if (!cerrada)
                {
                    // Ha dejado de estar pendiente mientras tanto: puede que el proveedor la acabe de aprobar
                    return Mal(HttpStatusCode.Conflict, "El pago a plazos de este pedido acaba de cambiar de estado. Consulte cómo está antes de pedirlo otra vez.");
                }
            }

            SolicitudPagoAPlazos solicitud = new SolicitudPagoAPlazos
            {
                Empresa = empresa,
                Pedido = pedido,
                Cliente = contexto.Pedido.Cliente,
                Contacto = contexto.Pedido.Contacto,
                Proveedor = proveedor.Codigo,
                Importe = importe,
                Canal = canal,
                Estado = EstadosPagoAPlazos.PENDIENTE,
                Pruebas = contexto.Configuracion.Pruebas,
                Usuario = usuario,
                Correo = correo,
                FechaCreacion = ahora
            };
            solicitud.Id = await repositorio.Crear(solicitud).ConfigureAwait(false);
            solicitud.Referencia = ReglasPagoAPlazos.Referencia(solicitud.Id);
            await repositorio.GuardarReferencias(solicitud.Id, solicitud.Referencia, null).ConfigureAwait(false);

            try
            {
                solicitud.ReferenciaExterna = await proveedor.CrearSolicitud(new PeticionSolicitudProveedor
                {
                    Referencia = solicitud.Referencia,
                    Pruebas = solicitud.Pruebas,
                    Canal = canal,
                    Empresa = empresa,
                    Pedido = pedido,
                    Cliente = solicitud.Cliente,
                    Correo = correo,
                    Importe = importe,
                    Lineas = contexto.Pedido.Lineas.Where(ReglasPagoAPlazos.SeFinancia).ToList(),
                    Facturacion = contexto.Pedido.Facturacion ?? contexto.Pedido.Envio,
                    Envio = contexto.Pedido.Envio ?? contexto.Pedido.Facturacion,
                    RecogidaEnTienda = contexto.Pedido.RecogidaEnTienda,
                    UrlNotificacion = UrlNotificacion(proveedor.Codigo)
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Sin solicitud en el proveedor no hay nada que aprobar: la nuestra se cierra
                _ = await repositorio.CambiarEstado(solicitud.Id, new[] { EstadosPagoAPlazos.PENDIENTE }, EstadosPagoAPlazos.CANCELADO, ahora,
                    "No se pudo abrir con el proveedor").ConfigureAwait(false);
                RegistrarError(new Exception($"[Pago a plazos #864] No se ha podido abrir con {proveedor.Nombre} el pago a plazos del pedido {pedido} " +
                    $"({importe:N2} EUR): {ex.Message}", ex));
                return Mal(HttpStatusCode.BadGateway, $"No se ha podido abrir el pago a plazos con {proveedor.Nombre}. Inténtelo de nuevo en unos minutos.");
            }
            await repositorio.GuardarReferencias(solicitud.Id, null, solicitud.ReferenciaExterna).ConfigureAwait(false);

            return ResultadoPagoAPlazos<SolicitudPagoAPlazosDTO>.Bien(await Entregar(solicitud, proveedor, aviso).ConfigureAwait(false));
        }

        /// <summary>Hace llegar la solicitud al cliente por su canal: la push de la app, o el enlace para quien pasa el pedido.</summary>
        private async Task<SolicitudPagoAPlazosDTO> Entregar(SolicitudPagoAPlazos solicitud, IProveedorPagoAPlazos proveedor, string aviso)
        {
            SolicitudPagoAPlazosDTO dto = new SolicitudPagoAPlazosDTO { Aviso = aviso };
            Rellenar(dto, solicitud, proveedor, Ahora());
            if (CanalesPagoAPlazos.Normalizar(solicitud.Canal) == CanalesPagoAPlazos.APP)
            {
                try
                {
                    dto.DispositivosAvisados = await avisos.AlCliente(solicitud, proveedor.Nombre).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    RegistrarError(new Exception($"[Pago a plazos #864] No se pudo avisar por push al cliente {solicitud.Cliente?.Trim()} del pago a " +
                        $"plazos del pedido {solicitud.Pedido}: {ex.Message}", ex));
                }
                if (dto.DispositivosAvisados == 0)
                {
                    // La push no ha salido: que quien pasa el pedido tenga el enlace para mandárselo
                    dto.UrlPago = proveedor.UrlPago(solicitud.ParaElProveedor(), CanalesPagoAPlazos.ENLACE);
                    dto.Aviso = ((aviso == null ? string.Empty : aviso + " ") +
                        "No se ha podido avisar al cliente en la app: mándele el enlace.").Trim();
                }
            }
            return dto;
        }

        #endregion

        #region Estado

        private void Rellenar(EstadoPagoAPlazosDTO dto, SolicitudPagoAPlazos s, IProveedorPagoAPlazos proveedor, DateTime ahora)
        {
            bool pendiente = EstadosPagoAPlazos.Es(s.Estado, EstadosPagoAPlazos.PENDIENTE);
            bool aprobada = EstadosPagoAPlazos.Es(s.Estado, EstadosPagoAPlazos.APROBADO);
            dto.Empresa = s.Empresa?.Trim();
            dto.Pedido = s.Pedido;
            dto.HaySolicitud = true;
            dto.IdSolicitud = s.Id;
            dto.Estado = s.Estado?.Trim();
            dto.Proveedor = s.Proveedor?.Trim();
            dto.NombreProveedor = proveedor?.Nombre ?? s.Proveedor?.Trim();
            dto.Canal = s.Canal?.Trim();
            dto.Importe = s.Importe;
            dto.ImporteCapturado = s.ImporteCapturado;
            dto.FechaSolicitud = s.FechaCreacion;
            dto.FechaAprobacion = s.FechaAprobacion;
            dto.FechaLimiteCaptura = s.FechaLimiteCaptura;
            dto.DiasParaCapturar = aprobada && s.FechaLimiteCaptura.HasValue ? ReglasPagoAPlazos.DiasHasta(s.FechaLimiteCaptura.Value, ahora) : (int?)null;
            dto.FechaCaptura = s.FechaCaptura;
            dto.FechaCierre = s.FechaCierre;
            dto.Motivo = s.Motivo?.Trim();
            dto.Usuario = s.Usuario?.Trim();
            dto.Pruebas = s.Pruebas;
            dto.UrlPago = pendiente && proveedor != null && !string.IsNullOrWhiteSpace(s.ReferenciaExterna)
                && CanalesPagoAPlazos.Normalizar(s.Canal) == CanalesPagoAPlazos.ENLACE
                ? proveedor.UrlPago(s.ParaElProveedor(), CanalesPagoAPlazos.ENLACE)
                : null;
        }

        public async Task<ResultadoPagoAPlazos<EstadoPagoAPlazosDTO>> Estado(string empresa, int pedido)
        {
            if (!repositorio.TablaDisponible())
            {
                return ResultadoPagoAPlazos<EstadoPagoAPlazosDTO>.Mal(HttpStatusCode.ServiceUnavailable, MENSAJE_SIN_SCRIPT);
            }
            empresa = EmpresaDe(empresa);
            SolicitudPagoAPlazos solicitud = ReglasPagoAPlazos.LaQueManda(await repositorio.LeerDelPedido(empresa, pedido).ConfigureAwait(false));
            if (solicitud == null)
            {
                if (await pedidos.Leer(empresa, pedido).ConfigureAwait(false) == null)
                {
                    return ResultadoPagoAPlazos<EstadoPagoAPlazosDTO>.Mal(HttpStatusCode.NotFound, $"No existe el pedido {pedido}.");
                }
                return ResultadoPagoAPlazos<EstadoPagoAPlazosDTO>.Bien(new EstadoPagoAPlazosDTO
                {
                    Empresa = empresa,
                    Pedido = pedido,
                    HaySolicitud = false,
                    Estado = EstadosPagoAPlazos.SIN_SOLICITUD
                });
            }
            EstadoPagoAPlazosDTO dto = new EstadoPagoAPlazosDTO();
            Rellenar(dto, solicitud, registro.PorCodigo(solicitud.Proveedor), Ahora());
            return ResultadoPagoAPlazos<EstadoPagoAPlazosDTO>.Bien(dto);
        }

        public async Task<ResultadoPagoAPlazos<PagoAPlazosClienteDTO>> ParaElCliente(string empresa, string cliente, int pedido)
        {
            if (!repositorio.TablaDisponible())
            {
                return ResultadoPagoAPlazos<PagoAPlazosClienteDTO>.Mal(HttpStatusCode.ServiceUnavailable, "El pago a plazos no está disponible ahora mismo.");
            }
            empresa = EmpresaDe(empresa);
            List<SolicitudPagoAPlazos> suyas = (await repositorio.LeerDelPedido(empresa, pedido).ConfigureAwait(false) ?? new List<SolicitudPagoAPlazos>())
                .Where(s => !string.IsNullOrWhiteSpace(cliente) && string.Equals(s.Cliente?.Trim(), cliente.Trim(), StringComparison.OrdinalIgnoreCase))
                .ToList();
            SolicitudPagoAPlazos solicitud = ReglasPagoAPlazos.LaQueManda(suyas);
            if (solicitud == null)
            {
                // La misma respuesta si no hay solicitud que si el pedido es de otro: no se dice qué hay ni de quién
                return ResultadoPagoAPlazos<PagoAPlazosClienteDTO>.Mal(HttpStatusCode.NotFound, "Este pedido no tiene ningún pago a plazos pendiente.");
            }
            IProveedorPagoAPlazos proveedor = registro.PorCodigo(solicitud.Proveedor);
            bool pendiente = EstadosPagoAPlazos.Es(solicitud.Estado, EstadosPagoAPlazos.PENDIENTE);
            return ResultadoPagoAPlazos<PagoAPlazosClienteDTO>.Bien(new PagoAPlazosClienteDTO
            {
                Empresa = solicitud.Empresa?.Trim(),
                Pedido = solicitud.Pedido,
                IdSolicitud = solicitud.Id,
                Estado = solicitud.Estado?.Trim(),
                Importe = solicitud.Importe,
                Proveedor = solicitud.Proveedor?.Trim(),
                NombreProveedor = proveedor?.Nombre ?? solicitud.Proveedor?.Trim(),
                UrlPago = pendiente && proveedor != null && !string.IsNullOrWhiteSpace(solicitud.ReferenciaExterna)
                    ? proveedor.UrlPago(solicitud.ParaElProveedor(), CanalesPagoAPlazos.APP)
                    : null,
                FechaSolicitud = solicitud.FechaCreacion
            });
        }

        #endregion

        #region Notificación del proveedor

        public async Task<RespuestaNotificacionProveedor> ProcesarNotificacion(string codigoProveedor, string cabeceraAutorizacion, string cuerpo)
        {
            IProveedorPagoAPlazos proveedor = registro.PorCodigo(codigoProveedor);
            if (proveedor == null)
            {
                RegistrarError(new Exception($"[Pago a plazos #864] Notificación de un proveedor que no existe: «{codigoProveedor}»."));
                return new RespuestaNotificacionProveedor { CodigoHttp = 404 };
            }
            NotificacionPagoAPlazos notificacion = proveedor.InterpretarNotificacion(cabeceraAutorizacion, cuerpo)
                ?? new NotificacionPagoAPlazos { Tipo = TipoNotificacionPagoAPlazos.Ilegible };
            DecisionNotificacionPagoAPlazos decision = await Decidir(proveedor, notificacion).ConfigureAwait(false);
            return proveedor.ResponderNotificacion(decision);
        }

        private async Task<DecisionNotificacionPagoAPlazos> Decidir(IProveedorPagoAPlazos proveedor, NotificacionPagoAPlazos notificacion)
        {
            string quien = $"[Pago a plazos #864] {proveedor.Nombre}";
            if (notificacion.Tipo == TipoNotificacionPagoAPlazos.NoAutorizada)
            {
                RegistrarError(new Exception($"{quien}: notificación con una autorización que no es la suya: rechazada."));
                return DecisionNotificacionPagoAPlazos.NoAutorizada;
            }
            if (notificacion.Tipo == TipoNotificacionPagoAPlazos.Ilegible || string.IsNullOrWhiteSpace(notificacion.Referencia))
            {
                RegistrarError(new Exception($"{quien}: notificación ilegible o sin referencia."));
                return DecisionNotificacionPagoAPlazos.Ilegible;
            }
            if (!repositorio.TablaDisponible())
            {
                RegistrarError(new Exception($"{quien}: notificación de {notificacion.Referencia} y la tabla no existe. {MENSAJE_SIN_SCRIPT}"));
                return DecisionNotificacionPagoAPlazos.Reintentar;
            }
            SolicitudPagoAPlazos solicitud = await repositorio.LeerPorReferencia(proveedor.Codigo, notificacion.Referencia.Trim()).ConfigureAwait(false);
            if (solicitud == null)
            {
                RegistrarError(new Exception($"{quien}: notificación de una referencia que no conocemos: {notificacion.Referencia} ({notificacion.Tipo})."));
                return DecisionNotificacionPagoAPlazos.Desconocida;
            }
            if (notificacion.Pruebas.HasValue && notificacion.Pruebas.Value != solicitud.Pruebas)
            {
                RegistrarError(new Exception($"{quien}: la notificación de {solicitud.Referencia} dice pruebas={notificacion.Pruebas} y la solicitud se " +
                    $"abrió con pruebas={solicitud.Pruebas}: rechazada."));
                return DecisionNotificacionPagoAPlazos.Ilegible;
            }

            bool importeExacto = notificacion.Importe.HasValue && notificacion.Importe.Value == solicitud.Importe;
            string estado = solicitud.Estado?.Trim();
            bool concedida = EstadosPagoAPlazos.EsConcedida(estado);
            bool pendiente = EstadosPagoAPlazos.Es(estado, EstadosPagoAPlazos.PENDIENTE);
            DateTime ahora = Ahora();

            switch (notificacion.Tipo)
            {
                case TipoNotificacionPagoAPlazos.PideConfirmacion:
                    if (!pendiente && !concedida)
                    {
                        return DecisionNotificacionPagoAPlazos.Rechazar; // cancelada, caducada o rechazada: no se confirma
                    }
                    if (!importeExacto)
                    {
                        // Un importe distinto del que se pidió es fraude o error, nunca un prepago
                        RegistrarError(new Exception($"{quien}: pide confirmar {notificacion.Importe} EUR de {solicitud.Referencia} y la solicitud es de " +
                            $"{solicitud.Importe:N2} EUR. NO se confirma."));
                        if (pendiente && await repositorio.CambiarEstado(solicitud.Id, new[] { EstadosPagoAPlazos.PENDIENTE }, EstadosPagoAPlazos.RECHAZADO,
                            ahora, "El importe de la confirmación no coincide").ConfigureAwait(false))
                        {
                            solicitud.Estado = EstadosPagoAPlazos.RECHAZADO;
                            await Avisar(() => avisos.Rechazado(solicitud, proveedor.Nombre), solicitud, "del rechazo").ConfigureAwait(false);
                            await Avisar(() => avisos.RechazadoAlCliente(solicitud, proveedor.Nombre, false), solicitud, "al cliente del rechazo")
                                .ConfigureAwait(false);
                        }
                        return DecisionNotificacionPagoAPlazos.Rechazar;
                    }
                    return await Aprobar(solicitud, proveedor, ahora).ConfigureAwait(false);

                case TipoNotificacionPagoAPlazos.Aprobada:
                    if (!importeExacto)
                    {
                        RegistrarError(new Exception($"{quien}: da por bueno un importe ({notificacion.Importe}) de {solicitud.Referencia} que no es el " +
                            $"de la solicitud ({solicitud.Importe:N2} EUR). Revisarlo en su panel."));
                    }
                    if (concedida || (pendiente && importeExacto))
                    {
                        // Pendiente: no nos llegó (o no contestamos) la petición de confirmación, pero el proveedor lo da por cerrado
                        _ = await Aprobar(solicitud, proveedor, ahora).ConfigureAwait(false);
                    }
                    else if (!pendiente)
                    {
                        RegistrarError(new Exception($"{quien}: da por pagada {solicitud.Referencia} (pedido {solicitud.Pedido}) y aquí está {estado}. " +
                            "Revisarlo en su panel (puede que haya que cancelarlo allí)."));
                    }
                    return DecisionNotificacionPagoAPlazos.Aceptar;

                case TipoNotificacionPagoAPlazos.Rechazada:
                    await Rechazar(solicitud, proveedor, notificacion.Motivo, ahora).ConfigureAwait(false);
                    return DecisionNotificacionPagoAPlazos.Aceptar;

                default:
                    return DecisionNotificacionPagoAPlazos.Aceptar; // paso intermedio: nada que hacer todavía
            }
        }

        /// <summary>
        /// Pendiente → Aprobado (UPDATE condicional) y el prepago en el pedido. Idempotente: si ya estaba aprobada (reintento del
        /// proveedor) solo se asegura de que el prepago está, sin duplicarlo ni volver a avisar.
        /// </summary>
        private async Task<DecisionNotificacionPagoAPlazos> Aprobar(SolicitudPagoAPlazos solicitud, IProveedorPagoAPlazos proveedor, DateTime ahora)
        {
            CondicionesProveedorPagoAPlazos condiciones = proveedor.Condiciones();
            PedidoPagoAPlazos pedido = await pedidos.Leer(solicitud.Empresa?.Trim(), solicitud.Pedido).ConfigureAwait(false);

            if (EstadosPagoAPlazos.Es(solicitud.Estado, EstadosPagoAPlazos.PENDIENTE))
            {
                string motivo = pedido == null
                    ? "El pedido ya no existe"
                    : ReglasPagoAPlazos.ImportePendiente(pedido) < solicitud.Importe
                        ? $"El pedido ha cambiado: le quedan por pagar {ReglasPagoAPlazos.Euros(ReglasPagoAPlazos.ImportePendiente(pedido))} y " +
                          $"se pidió financiar {ReglasPagoAPlazos.Euros(solicitud.Importe)}"
                        : null;
                if (motivo != null)
                {
                    if (await repositorio.CambiarEstado(solicitud.Id, new[] { EstadosPagoAPlazos.PENDIENTE }, EstadosPagoAPlazos.CANCELADO, ahora, motivo)
                        .ConfigureAwait(false))
                    {
                        solicitud.Estado = EstadosPagoAPlazos.CANCELADO;
                        RegistrarError(new Exception($"[Pago a plazos #864] {proveedor.Nombre} aprueba {solicitud.Referencia} (pedido {solicitud.Pedido}) " +
                            $"pero NO se confirma: {motivo}."));
                        await Avisar(() => avisos.Perdido(solicitud, proveedor.Nombre,
                            $"{motivo}. No se ha confirmado el pago: pídalo de nuevo si hace falta."), solicitud, "de que no se confirma").ConfigureAwait(false);
                    }
                    return DecisionNotificacionPagoAPlazos.Rechazar;
                }

                DateTime limite = ahora.AddDays(condiciones.DiasPlazoCaptura);
                bool aprobada = await repositorio.CambiarEstado(solicitud.Id, new[] { EstadosPagoAPlazos.PENDIENTE }, EstadosPagoAPlazos.APROBADO, ahora,
                    null, limite).ConfigureAwait(false);
                if (aprobada)
                {
                    solicitud.Estado = EstadosPagoAPlazos.APROBADO;
                    solicitud.FechaAprobacion = ahora;
                    solicitud.FechaLimiteCaptura = limite;
                }
                else
                {
                    // Ha cambiado entre la lectura y la escritura: se decide con lo que hay ahora
                    solicitud = await repositorio.Leer(solicitud.Id).ConfigureAwait(false);
                    if (solicitud == null || !EstadosPagoAPlazos.EsConcedida(solicitud.Estado))
                    {
                        return DecisionNotificacionPagoAPlazos.Rechazar;
                    }
                }
            }

            await AsegurarPrepago(solicitud, proveedor, condiciones, pedido, ahora).ConfigureAwait(false);
            return DecisionNotificacionPagoAPlazos.Aceptar;
        }

        private async Task AsegurarPrepago(SolicitudPagoAPlazos solicitud, IProveedorPagoAPlazos proveedor, CondicionesProveedorPagoAPlazos condiciones,
            PedidoPagoAPlazos pedido, DateTime ahora)
        {
            if (EstadosPagoAPlazos.Es(solicitud.Estado, EstadosPagoAPlazos.CAPTURADO) && solicitud.PrepagoId.HasValue)
            {
                return; // ya cobrada y con su prepago: nada que asegurar
            }
            string concepto = ReglasPagoAPlazos.ConceptoPrepago(condiciones.PrefijoConceptoPrepago, solicitud.Pedido, solicitud.Referencia);
            // Si esto falla, la excepción sube: el proveedor reintenta y, ya aprobada, se vuelve a intentar el prepago
            PrepagoPagoAPlazos prepago = await pedidos.AnadirPrepago(solicitud.Empresa?.Trim(), solicitud.Pedido, solicitud.Importe,
                condiciones.CuentaPrepago, concepto, solicitud.Usuario, solicitud.FechaAprobacion ?? ahora).ConfigureAwait(false);
            if (prepago == null)
            {
                RegistrarError(new Exception($"[Pago a plazos #864] {proveedor.Nombre} ha aprobado {solicitud.Referencia} ({solicitud.Importe:N2} EUR, " +
                    $"cliente {solicitud.Cliente?.Trim()}) pero el pedido {solicitud.Pedido} NO existe: el prepago no se ha creado. Cancelarlo en su panel."));
                return;
            }
            if (prepago.Creado || solicitud.PrepagoId != prepago.Id)
            {
                await repositorio.GuardarPrepago(solicitud.Id, prepago.Id).ConfigureAwait(false);
                solicitud.PrepagoId = prepago.Id;
            }
            if (prepago.Creado)
            {
                decimal falta = pedido == null ? 0 : ReglasPagoAPlazos.ImportePendiente(pedido) - solicitud.Importe;
                bool contraReembolso = pedido != null && pedido.ContraReembolso;
                await Avisar(() => avisos.Aprobado(solicitud, proveedor.Nombre, falta < 0 ? 0 : falta, contraReembolso), solicitud, "de la aprobación")
                    .ConfigureAwait(false);
                // Al cliente también (Dirección 13/11/26). Cuelga de lo mismo que el del vendedor: el prepago recién creado, que solo
                // pasa una vez por solicitud (su concepto es único), así que una notificación repetida del proveedor no lo repite; y
                // si el prepago falló al aprobar, el aviso sale en el reintento, cuando ya es verdad que está todo hecho.
                await Avisar(() => avisos.AprobadoAlCliente(solicitud, proveedor.Nombre), solicitud, "al cliente de la aprobación").ConfigureAwait(false);
            }
        }

        private async Task Rechazar(SolicitudPagoAPlazos solicitud, IProveedorPagoAPlazos proveedor, string motivoProveedor, DateTime ahora)
        {
            string motivo = string.IsNullOrWhiteSpace(motivoProveedor) ? $"Rechazada por {proveedor.Nombre}" : $"Rechazada por {proveedor.Nombre}: {motivoProveedor.Trim()}";
            if (EstadosPagoAPlazos.Es(solicitud.Estado, EstadosPagoAPlazos.PENDIENTE))
            {
                if (await repositorio.CambiarEstado(solicitud.Id, new[] { EstadosPagoAPlazos.PENDIENTE }, EstadosPagoAPlazos.RECHAZADO, ahora, motivo)
                    .ConfigureAwait(false))
                {
                    solicitud.Estado = EstadosPagoAPlazos.RECHAZADO;
                    await Avisar(() => avisos.Rechazado(solicitud, proveedor.Nombre), solicitud, "del rechazo").ConfigureAwait(false);
                    await Avisar(() => avisos.RechazadoAlCliente(solicitud, proveedor.Nombre, false), solicitud, "al cliente del rechazo")
                        .ConfigureAwait(false);
                }
                return;
            }
            if (!EstadosPagoAPlazos.Es(solicitud.Estado, EstadosPagoAPlazos.APROBADO))
            {
                return; // ya estaba cerrada (idempotente) o ya cobrada (eso se mira en el panel del proveedor)
            }
            if (await repositorio.CambiarEstado(solicitud.Id, new[] { EstadosPagoAPlazos.APROBADO }, EstadosPagoAPlazos.RECHAZADO, ahora, motivo)
                .ConfigureAwait(false))
            {
                solicitud.Estado = EstadosPagoAPlazos.RECHAZADO;
                bool quitado = await QuitarPrepago(solicitud).ConfigureAwait(false);
                RegistrarError(new Exception($"[Pago a plazos #864] {proveedor.Nombre} rechaza {solicitud.Referencia} (pedido {solicitud.Pedido}, " +
                    $"{solicitud.Importe:N2} EUR) DESPUÉS de haberlo aprobado. " +
                    (quitado ? "Se ha quitado el prepago del pedido." : "El prepago NO se ha podido quitar (¿ya facturado?): revisarlo.")));
                await Avisar(() => avisos.Perdido(solicitud, proveedor.Nombre, $"{proveedor.Nombre} ha rechazado el pago después de aprobarlo. " +
                    TextoPrepago(quitado)), solicitud, "del rechazo").ConfigureAwait(false);
                // A ese cliente se le dijo que estaba aprobado: que sepa que ya no
                await Avisar(() => avisos.RechazadoAlCliente(solicitud, proveedor.Nombre, true), solicitud, "al cliente del rechazo")
                    .ConfigureAwait(false);
            }
        }

        private static string TextoPrepago(bool quitado) => quitado
            ? "Se le ha quitado el prepago: el pedido vuelve a estar sin pagar."
            : "El prepago no se ha podido quitar del pedido: revíselo con Administración.";

        private async Task<bool> QuitarPrepago(SolicitudPagoAPlazos solicitud)
        {
            if (!solicitud.PrepagoId.HasValue)
            {
                return false;
            }
            try
            {
                return await pedidos.QuitarPrepago(solicitud.PrepagoId.Value).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                RegistrarError(new Exception($"[Pago a plazos #864] No se ha podido quitar el prepago {solicitud.PrepagoId} del pedido {solicitud.Pedido}: {ex.Message}", ex));
                return false;
            }
        }

        /// <summary>Un aviso nunca rompe lo que se estaba haciendo.</summary>
        private async Task<bool> Avisar(Func<Task> aviso, SolicitudPagoAPlazos solicitud, string que)
        {
            try
            {
                await aviso().ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                RegistrarError(new Exception($"[Pago a plazos #864] No se ha podido avisar {que} del pago a plazos del pedido {solicitud?.Pedido}: {ex.Message}", ex));
                return false;
            }
        }

        #endregion

        #region Cancelar

        public async Task<ResultadoPagoAPlazos<EstadoPagoAPlazosDTO>> Cancelar(string empresa, int pedido, string motivo, string usuario)
        {
            ResultadoPagoAPlazos<EstadoPagoAPlazosDTO> Mal(HttpStatusCode codigo, string mensaje)
                => ResultadoPagoAPlazos<EstadoPagoAPlazosDTO>.Mal(codigo, mensaje);

            if (!repositorio.TablaDisponible())
            {
                return Mal(HttpStatusCode.ServiceUnavailable, MENSAJE_SIN_SCRIPT);
            }
            empresa = EmpresaDe(empresa);
            List<SolicitudPagoAPlazos> solicitudes = await repositorio.LeerDelPedido(empresa, pedido).ConfigureAwait(false) ?? new List<SolicitudPagoAPlazos>();
            if (solicitudes.Count == 0 && await pedidos.Leer(empresa, pedido).ConfigureAwait(false) == null)
            {
                return Mal(HttpStatusCode.NotFound, $"No existe el pedido {pedido}.");
            }
            SolicitudPagoAPlazos solicitud = ReglasPagoAPlazos.LaQueManda(solicitudes);
            if (solicitud == null || !EstadosPagoAPlazos.EsViva(solicitud.Estado))
            {
                return Mal(HttpStatusCode.Conflict, solicitud != null && EstadosPagoAPlazos.Es(solicitud.Estado, EstadosPagoAPlazos.CAPTURADO)
                    ? "El pago a plazos de este pedido ya está cobrado: no se puede cancelar desde aquí."
                    : "Este pedido no tiene ningún pago a plazos que cancelar.");
            }
            IProveedorPagoAPlazos proveedor = registro.PorCodigo(solicitud.Proveedor);
            string texto = $"Cancelado por {UsuarioAuditoriaHelper.SinDominio(usuario) ?? "un usuario"}" + (string.IsNullOrWhiteSpace(motivo) ? string.Empty : $": {motivo.Trim()}");
            string error = await CerrarViva(solicitud, proveedor, EstadosPagoAPlazos.CANCELADO, texto, quitarPrepago: true).ConfigureAwait(false);
            if (error != null)
            {
                return Mal(HttpStatusCode.BadGateway, error);
            }
            return await Estado(empresa, pedido).ConfigureAwait(false);
        }

        public async Task CancelarPorPedidoBorrado(string empresa, int pedido, string usuario)
        {
            try
            {
                if (!repositorio.TablaDisponible())
                {
                    return;
                }
                empresa = EmpresaDe(empresa);
                List<SolicitudPagoAPlazos> vivas = (await repositorio.LeerDelPedido(empresa, pedido).ConfigureAwait(false) ?? new List<SolicitudPagoAPlazos>())
                    .Where(s => EstadosPagoAPlazos.EsViva(s.Estado)).ToList();
                foreach (SolicitudPagoAPlazos solicitud in vivas)
                {
                    string error = await CerrarViva(solicitud, registro.PorCodigo(solicitud.Proveedor), EstadosPagoAPlazos.CANCELADO,
                        $"El pedido se ha borrado ({UsuarioAuditoriaHelper.SinDominio(usuario) ?? "sin usuario"})", quitarPrepago: false).ConfigureAwait(false);
                    if (error != null)
                    {
                        // El job lo volverá a intentar (el pedido ya no existe); mientras, que se sepa
                        RegistrarError(new Exception($"[Pago a plazos #864] Pedido {pedido} borrado con un pago a plazos aprobado " +
                            $"({solicitud.Referencia}, {solicitud.Importe:N2} EUR) y NO se ha podido cancelar: {error}"));
                    }
                }
            }
            catch (Exception ex)
            {
                RegistrarError(new Exception($"[Pago a plazos #864] No se han podido cancelar los pagos a plazos del pedido {pedido} al borrarlo: {ex.Message}", ex));
            }
        }

        /// <summary>
        /// Cierra una solicitud viva. Pendiente: solo aquí (el proveedor no ha aprobado nada; si aprobara después, se le dice que
        /// no). Aprobada: primero se cancela en el proveedor y, si eso falla, no se toca nada. Devuelve el error (de usted) o null.
        /// </summary>
        private async Task<string> CerrarViva(SolicitudPagoAPlazos solicitud, IProveedorPagoAPlazos proveedor, string estadoFinal, string motivo,
            bool quitarPrepago)
        {
            DateTime ahora = Ahora();
            if (EstadosPagoAPlazos.Es(solicitud.Estado, EstadosPagoAPlazos.PENDIENTE))
            {
                if (await repositorio.CambiarEstado(solicitud.Id, new[] { EstadosPagoAPlazos.PENDIENTE }, estadoFinal, ahora, motivo).ConfigureAwait(false))
                {
                    solicitud.Estado = estadoFinal;
                    return null;
                }
                solicitud = await repositorio.Leer(solicitud.Id).ConfigureAwait(false);
                if (solicitud == null || !EstadosPagoAPlazos.Es(solicitud.Estado, EstadosPagoAPlazos.APROBADO))
                {
                    return null; // ya estaba cerrada
                }
            }
            if (!EstadosPagoAPlazos.Es(solicitud.Estado, EstadosPagoAPlazos.APROBADO))
            {
                return null;
            }
            string nombre = proveedor?.Nombre ?? solicitud.Proveedor?.Trim();
            if (proveedor == null)
            {
                return $"El proveedor «{nombre}» ya no está dado de alta: cancele la financiación en su panel.";
            }
            try
            {
                await proveedor.Cancelar(solicitud.ParaElProveedor()).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                RegistrarError(new Exception($"[Pago a plazos #864] NO se ha podido cancelar en {nombre} {solicitud.Referencia} (pedido {solicitud.Pedido}, " +
                    $"{solicitud.Importe:N2} EUR): {ex.Message}. Motivo de la cancelación: {motivo}.", ex));
                return $"No se ha podido cancelar la financiación en {nombre}. Inténtelo de nuevo en unos minutos.";
            }
            if (await repositorio.CambiarEstado(solicitud.Id, new[] { EstadosPagoAPlazos.APROBADO }, estadoFinal, ahora, motivo).ConfigureAwait(false))
            {
                solicitud.Estado = estadoFinal;
                bool quitado = quitarPrepago && await QuitarPrepago(solicitud).ConfigureAwait(false);
                RegistrarError(new Exception($"[Pago a plazos #864] {solicitud.Referencia} (pedido {solicitud.Pedido}, {solicitud.Importe:N2} EUR, cliente " +
                    $"{solicitud.Cliente?.Trim()}) cancelada en {nombre}: {motivo}." + (quitarPrepago ? " " + TextoPrepago(quitado) : string.Empty)));
                await Avisar(() => avisos.Perdido(solicitud, nombre, $"{motivo}." + (quitarPrepago ? " " + TextoPrepago(quitado) : string.Empty)),
                    solicitud, "de la cancelación").ConfigureAwait(false);
            }
            return null;
        }

        #endregion

        #region Job: capturas, avisos de plazo, caducidades

        public async Task<ResultadoPlazosPagoAPlazos> ProcesarPlazos(DateTime ahora)
        {
            ResultadoPlazosPagoAPlazos resultado = new ResultadoPlazosPagoAPlazos();
            if (!repositorio.TablaDisponible())
            {
                return resultado;
            }
            ConfiguracionPagoAPlazos configuracion = leerConfiguracion() ?? new ConfiguracionPagoAPlazos();
            List<SolicitudPagoAPlazos> vivas = await repositorio.LeerVivas().ConfigureAwait(false) ?? new List<SolicitudPagoAPlazos>();
            foreach (SolicitudPagoAPlazos solicitud in vivas)
            {
                try
                {
                    await ProcesarPlazo(solicitud, configuracion, ahora, resultado).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    resultado.Fallidas.Add(solicitud.Id);
                    RegistrarError(new Exception($"[Pago a plazos #864] Fallo al repasar {solicitud.Referencia} (pedido {solicitud.Pedido}): {ex.Message}", ex));
                }
            }
            return resultado;
        }

        private async Task ProcesarPlazo(SolicitudPagoAPlazos solicitud, ConfiguracionPagoAPlazos configuracion, DateTime ahora, ResultadoPlazosPagoAPlazos resultado)
        {
            IProveedorPagoAPlazos proveedor = registro.PorCodigo(solicitud.Proveedor);
            string nombre = proveedor?.Nombre ?? solicitud.Proveedor?.Trim();
            PedidoPagoAPlazos pedido = await pedidos.Leer(solicitud.Empresa?.Trim(), solicitud.Pedido).ConfigureAwait(false);

            if (EstadosPagoAPlazos.Es(solicitud.Estado, EstadosPagoAPlazos.PENDIENTE))
            {
                if (pedido == null)
                {
                    if (await repositorio.CambiarEstado(solicitud.Id, new[] { EstadosPagoAPlazos.PENDIENTE }, EstadosPagoAPlazos.CANCELADO, ahora,
                        "El pedido ya no existe").ConfigureAwait(false))
                    {
                        resultado.Canceladas.Add(solicitud.Id);
                    }
                }
                else if (ReglasPagoAPlazos.PendienteCaducada(solicitud, ahora, configuracion.DiasValidezSolicitud)
                    && await repositorio.CambiarEstado(solicitud.Id, new[] { EstadosPagoAPlazos.PENDIENTE }, EstadosPagoAPlazos.CADUCADO, ahora,
                        $"El cliente no terminó el pago en {configuracion.DiasValidezSolicitud} días").ConfigureAwait(false))
                {
                    resultado.Caducadas.Add(solicitud.Id);
                }
                return;
            }

            switch (ReglasPagoAPlazos.DecidirPlazo(solicitud, pedido, ahora, configuracion.DiasAviso))
            {
                case AccionPlazoPagoAPlazos.Capturar:
                    if (await Capturar(solicitud, proveedor, pedido, ahora, cierraElPlazo: false).ConfigureAwait(false))
                    {
                        resultado.Capturadas.Add(solicitud.Id);
                    }
                    break;

                case AccionPlazoPagoAPlazos.CapturarLoServido:
                    if (await Capturar(solicitud, proveedor, pedido, ahora, cierraElPlazo: true).ConfigureAwait(false))
                    {
                        resultado.Capturadas.Add(solicitud.Id);
                    }
                    break;

                case AccionPlazoPagoAPlazos.Avisar:
                    int dias = ReglasPagoAPlazos.DiasHasta(solicitud.FechaLimiteCaptura.Value, ahora);
                    if (await Avisar(() => avisos.PlazoCerca(solicitud, nombre, dias), solicitud, "del plazo").ConfigureAwait(false))
                    {
                        await repositorio.MarcarAvisoPlazo(solicitud.Id, ahora).ConfigureAwait(false);
                        resultado.Avisadas.Add(solicitud.Id);
                    }
                    break;

                case AccionPlazoPagoAPlazos.Caducar:
                    string errorCaducar = await CerrarViva(solicitud, proveedor, EstadosPagoAPlazos.CADUCADO,
                        $"El pedido no ha salido en el plazo de {nombre}", quitarPrepago: true).ConfigureAwait(false);
                    (errorCaducar == null ? resultado.Caducadas : resultado.Fallidas).Add(solicitud.Id);
                    break;

                case AccionPlazoPagoAPlazos.CancelarSinPedido:
                    string errorCancelar = await CerrarViva(solicitud, proveedor, EstadosPagoAPlazos.CANCELADO,
                        pedido == null ? "El pedido ya no existe" : "El pedido se ha quedado sin líneas", quitarPrepago: pedido != null).ConfigureAwait(false);
                    (errorCancelar == null ? resultado.Canceladas : resultado.Fallidas).Add(solicitud.Id);
                    break;
            }
        }

        /// <summary>
        /// Confirma el cobro al proveedor por lo servido (nunca más de lo aprobado) y una sola vez: antes se le pregunta qué
        /// tiene ya cobrado, como el job de #663. <paramref name="cierraElPlazo"/>: se acabó el plazo con el pedido a medio
        /// servir; se cobra lo servido, el prepago se deja en eso y se avisa de que el resto queda sin pagar.
        /// </summary>
        private async Task<bool> Capturar(SolicitudPagoAPlazos solicitud, IProveedorPagoAPlazos proveedor, PedidoPagoAPlazos pedido, DateTime ahora,
            bool cierraElPlazo)
        {
            if (proveedor == null)
            {
                throw new InvalidOperationException($"el proveedor «{solicitud.Proveedor?.Trim()}» ya no está dado de alta");
            }
            ReferenciaSolicitudProveedor referencia = solicitud.ParaElProveedor();
            EstadoCapturaProveedor estado = await proveedor.ConsultarCaptura(referencia).ConfigureAwait(false);
            if (estado != null && (estado.Capturado > 0 || (estado.PendienteDeCapturar.HasValue && estado.PendienteDeCapturar.Value <= 0)))
            {
                // Ya está cobrada (otra pasada, o a mano en el panel): se apunta y no se vuelve a capturar
                _ = await repositorio.GuardarCaptura(solicitud.Id, estado.FechaUltimaCaptura ?? ahora, estado.Capturado).ConfigureAwait(false);
                return true;
            }

            decimal servido = ReglasPagoAPlazos.ImporteServido(pedido);
            decimal importe = Math.Min(Math.Min(servido, solicitud.Importe), estado?.PendienteDeCapturar ?? solicitud.Importe);
            if (importe <= 0)
            {
                RegistrarError(new Exception($"[Pago a plazos #864] {solicitud.Referencia}: el pedido {solicitud.Pedido} está servido pero no hay nada " +
                    $"que cobrar (servido {servido:N2} EUR, aprobado {solicitud.Importe:N2} EUR). Revisarlo."));
                return false;
            }

            await proveedor.Capturar(referencia, importe).ConfigureAwait(false);
            string motivo = cierraElPlazo ? $"Plazo vencido con el pedido a medio servir: cobrado lo servido ({ReglasPagoAPlazos.Euros(importe)})" : null;
            _ = await repositorio.GuardarCaptura(solicitud.Id, ahora, importe, motivo).ConfigureAwait(false);

            if (importe < solicitud.Importe)
            {
                RegistrarError(new Exception($"[Pago a plazos #864] {solicitud.Referencia}: cobrados {importe:N2} EUR de {solicitud.Importe:N2} EUR aprobados " +
                    $"(pedido {solicitud.Pedido}" + (cierraElPlazo ? ", plazo vencido a medio servir" : ", ha salido por menos") +
                    $"). Revisar en el panel de {proveedor.Nombre} si hay que anular el resto."));
            }
            if (cierraElPlazo && importe < solicitud.Importe)
            {
                bool ajustado = false;
                if (solicitud.PrepagoId.HasValue)
                {
                    try
                    {
                        ajustado = await pedidos.AjustarPrepago(solicitud.PrepagoId.Value, importe).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        RegistrarError(new Exception($"[Pago a plazos #864] No se ha podido ajustar el prepago {solicitud.PrepagoId} del pedido {solicitud.Pedido}: {ex.Message}", ex));
                    }
                }
                await Avisar(() => avisos.Perdido(solicitud, proveedor.Nombre,
                    $"se acabó el plazo con el pedido a medio servir y solo se ha cobrado lo servido ({ReglasPagoAPlazos.Euros(importe)}). " +
                    (ajustado ? "El prepago se ha dejado en ese importe: el resto del pedido queda sin pagar."
                              : "El prepago no se ha podido ajustar: revíselo con Administración.")),
                    solicitud, "del plazo vencido").ConfigureAwait(false);
            }
            return true;
        }

        #endregion
    }
}
