using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using NestoAPI.Infraestructure.EnlacesDescarga;
using NestoAPI.Infraestructure.Informes;
using NestoAPI.Infraestructure.Pagos;
using NestoAPI.Models;
using NestoAPI.Models.Pagos;

namespace NestoAPI.Infraestructure.Agencias.RepartoPropio
{
    // NestoAPI#773 (Carlos 31/10/26): el repartidor cobra con tarjeta mandando un enlace Redsys por WhatsApp (o por correo).

    /// <summary>Para qué es el enlace del reparto.</summary>
    public static class TiposCobroTarjetaReparto
    {
        /// <summary>El reembolso de un envío (al entregarlo).</summary>
        public const string REEMBOLSO = "Reembolso";
        /// <summary>Efectos pendientes de un cliente («Cobrar una deuda», #766).</summary>
        public const string DEUDA = "Deuda";
    }

    /// <summary>Cómo está el enlace (toda la cadena: si Redsys lo deniega, ServicioPagos genera otro con PagoOriginalId).</summary>
    public static class EstadosEnlaceTarjeta
    {
        public const string PENDIENTE = "Pendiente";
        public const string PAGADO = "Pagado";
        public const string DENEGADO = "Denegado";
        public const string ANULADO = "Anulado";
    }

    /// <summary>POST api/RepartoPropio/Envios/{envioId}/EnlaceTarjeta. Todo opcional.</summary>
    public class PeticionEnlaceTarjetaDTO
    {
        /// <summary>Si viene, el enlace se manda también a este correo («Enviar por correo»).</summary>
        public string Correo { get; set; }
    }

    /// <summary>POST api/RepartoPropio/Cobros/EnlaceTarjeta: los efectos elegidos de un cliente (por su suma).</summary>
    public class PeticionEnlaceTarjetaDeudaDTO
    {
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        /// <summary>Los Nº Orden de ExtractoCliente (al menos uno).</summary>
        public List<int> Efectos { get; set; }
        public string Correo { get; set; }
    }

    /// <summary>La respuesta de los dos POST: el enlace y lo que hace falta para mandarlo por WhatsApp.</summary>
    public class EnlaceTarjetaRepartoDTO
    {
        /// <summary>El PagoTPV del enlace (el raíz de la cadena): con él se pregunta el estado y se entra en el grupo de SignalR.</summary>
        public int PagoTPVId { get; set; }
        public int? EnvioId { get; set; }
        public string Cliente { get; set; }
        public decimal Importe { get; set; }
        /// <summary>La página de pago propia (/pago/{token}).</summary>
        public string Url { get; set; }
        /// <summary>El mensaje de WhatsApp, de usted, ya relleno.</summary>
        public string Texto { get; set; }
        /// <summary>Los móviles en formato internacional (34600123456): el del envío el primero. Vacío si no hay ninguno.</summary>
        public List<string> Moviles { get; set; } = new List<string>();
        /// <summary>El correo que se propone (el del envío o el del cliente), o null.</summary>
        public string Correo { get; set; }
        /// <summary><see cref="EstadosEnlaceTarjeta"/>.</summary>
        public string Estado { get; set; }
        /// <summary>Ya había un enlace vivo y se devuelve ese (no se crea otro).</summary>
        public bool Reutilizado { get; set; }
        /// <summary>Se ha mandado por correo en esta petición.</summary>
        public bool CorreoEnviado { get; set; }
        /// <summary>El grupo del AlmacenHub en el que se avisa del pago («PagoTPV:123»).</summary>
        public string GrupoAvisos { get; set; }
    }

    /// <summary>GET api/RepartoPropio/EnlaceTarjeta/{pagoTPVId}/Estado.</summary>
    public class EstadoEnlaceTarjetaDTO
    {
        public int PagoTPVId { get; set; }
        public int? EnvioId { get; set; }
        public string Cliente { get; set; }
        public string Tipo { get; set; }
        public decimal Importe { get; set; }
        /// <summary><see cref="EstadosEnlaceTarjeta"/>.</summary>
        public string Estado { get; set; }
        public bool Pagado { get; set; }
    }

    /// <summary>El evento «pagoCompletado» del AlmacenHub (grupo «PagoTPV:{PagoTPVId}»).</summary>
    public class PagoCompletadoRepartoDTO
    {
        /// <summary>El PagoTPV raíz (el que devolvió el POST del enlace).</summary>
        public int PagoTPVId { get; set; }
        public int? EnvioId { get; set; }
        public string Cliente { get; set; }
        public decimal Importe { get; set; }
        /// <summary><see cref="TiposCobroTarjetaReparto"/>.</summary>
        public string Tipo { get; set; }
        public DateTime FechaHora { get; set; }
    }

    /// <summary>Una fila de RepartoCobrosTarjeta (Scripts/Issue773_CobroTarjetaReparto.sql).</summary>
    public class EnlaceTarjetaReparto
    {
        public int Id { get; set; }
        public int PagoTPVId { get; set; }
        public string Tipo { get; set; }
        public int? EnvioId { get; set; }
        public string Empresa { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public decimal Importe { get; set; }
        /// <summary>Los efectos de una deuda, ordenados y separados por comas («123,456»); null en los reembolsos.</summary>
        public string Efectos { get; set; }
        public string Usuario { get; set; }
        public string Dispositivo { get; set; }
        public DateTime FechaCreacion { get; set; }
    }

    /// <summary>Un PagoTPV de la cadena del enlace (el raíz y los que se regeneran al denegarse).</summary>
    public class PagoCadenaReparto
    {
        public int Id { get; set; }
        public int? PagoOriginalId { get; set; }
        public string Estado { get; set; }
        public decimal Importe { get; set; }
        public DateTime FechaCreacion { get; set; }
        public Guid TokenAcceso { get; set; }
    }

    /// <summary>Qué hará el enlace del reembolso al pagarse: liquidar ese efecto o, sin él, quedar a cuenta (y por qué).</summary>
    public class LiquidacionReembolsoTarjeta
    {
        public EfectoAPagar Efecto { get; set; }
        public string MotivoACuenta { get; set; }
    }

    /// <summary>Lo que se sabe del cliente para mandarle el enlace.</summary>
    public class DatosContactoCobro
    {
        public string Nombre { get; set; }
        public string Pais { get; set; }
        public List<string> Telefonos { get; set; } = new List<string>();
        public string Correo { get; set; }
    }

    /// <summary>Lo que el servicio lee y escribe (detrás de una interfaz para probar las reglas sin BD).</summary>
    public interface IRepositorioCobrosTarjetaReparto
    {
        /// <summary>¿Se ha lanzado Scripts/Issue773_CobroTarjetaReparto.sql (tabla RepartoCobrosTarjeta)?</summary>
        bool ScriptAplicado();
        /// <summary>El último enlace del reparto para el reembolso de ese envío, o null.</summary>
        Task<EnlaceTarjetaReparto> LeerUltimoDelEnvio(int envioId);
        /// <summary>El último enlace del reparto para esos efectos de ese cliente, o null.</summary>
        Task<EnlaceTarjetaReparto> LeerUltimoDeDeuda(string cliente, string efectos);
        /// <summary>El enlace del reparto de ese PagoTPV raíz, o null (no es del reparto).</summary>
        Task<EnlaceTarjetaReparto> LeerPorPago(int pagoTPVId);
        /// <summary>El PagoTPV raíz y los regenerados a partir de él.</summary>
        Task<List<PagoCadenaReparto>> LeerCadena(int pagoRaiz);
        Task Guardar(EnlaceTarjetaReparto enlace);
        Task<DatosContactoCobro> LeerContacto(string empresa, string cliente, string contacto);
        Task<List<EfectoCobroReparto>> LeerEfectos(IReadOnlyCollection<int> ids);
        /// <summary>Manda el enlace (ya creado) al correo, con el correo de siempre de los enlaces. False si no sale.</summary>
        Task<bool> MandarPorCorreo(int pagoTPVId, string correo);
        /// <summary>
        /// El efecto (ExtractoCliente pendiente) que liquidaría el reembolso del envío por ese importe: el MISMO que elige el cobro en
        /// efectivo (TramitacionEnviosService.BuscarMovimientoALiquidarAsync). Null si no hay ninguno.
        /// </summary>
        Task<EfectoCobroReparto> LeerEfectoDelReembolso(EnviosAgencia envio, decimal importe);
        /// <summary>Una fila en EnviosHistoria (fuera de cualquier transacción).</summary>
        Task AnotarHistoria(EnvioHistoria fila);
    }

    /// <summary>
    /// NestoAPI#773: las reglas del cobro con tarjeta del reparto propio. El enlace es el de siempre (ServicioPagos.IniciarPago, la
    /// página propia /pago/{token}); aquí solo se decide si se puede, se reutiliza el vivo y se compone el WhatsApp. El dinero lo
    /// contabiliza ServicioPagos al pagarse (ContabilizarCobro: liquidando los efectos del enlace —el de la factura del envío en los
    /// reembolsos, el mismo que liquidaría el efectivo— o, sin efectos, a cuenta del cliente); la entrega
    /// con «Tarjeta» no contabiliza nada (ver <see cref="ComprobadorCobroEntrega"/>).
    /// </summary>
    public class ServicioCobroTarjetaReparto
    {
        public const string MENSAJE_FALTA_SCRIPT = "Falta lanzar el script Scripts/Issue773_CobroTarjetaReparto.sql (cobro con tarjeta del " +
            "reparto): hasta entonces cobre en efectivo. Avise a Informática.";
        public const string MENSAJE_EMPRESA_EFECTIVO = "Los envíos y clientes de la empresa {0} se cobran siempre en efectivo: no se puede mandar " +
            "un enlace de pago.";
        public const int MAXIMO_EFECTOS = 50;

        private static readonly CultureInfo Espanol = new CultureInfo("es-ES");

        private readonly IRepositorioRepartoPropio reparto;
        private readonly IRepositorioCobrosTarjetaReparto repositorio;
        private readonly Func<IServicioPagos> pagos;
        private readonly Func<DateTime> ahora;

        public ServicioCobroTarjetaReparto(IRepositorioRepartoPropio reparto, IRepositorioCobrosTarjetaReparto repositorio,
            Func<IServicioPagos> pagos = null, Func<DateTime> ahora = null)
        {
            this.reparto = reparto ?? throw new ArgumentNullException(nameof(reparto));
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
            this.pagos = pagos ?? (() => new ServicioPagos(new RedsysService(), new Contabilidad.ContabilidadService(), new LectorParametrosUsuario()));
            this.ahora = ahora ?? (() => DateTime.Now);
        }

        /// <summary>«PagoTPV:123»: el grupo del AlmacenHub donde espera la PDA que lo ha generado.</summary>
        public static string GrupoAvisos(int pagoTPVId) => Notificaciones.AvisosAlmacen.GrupoPagoTPV(pagoTPVId);

        // ---------------------------------------------------------------- el reembolso de un envío

        public async Task<ResultadoReparto<EnlaceTarjetaRepartoDTO>> EnlaceEnvio(int envioId, PeticionEnlaceTarjetaDTO peticion, string usuario,
            string dispositivo)
        {
            if (!repositorio.ScriptAplicado())
            {
                return Invalido(MENSAJE_FALTA_SCRIPT);
            }
            string correo = Limpio(peticion?.Correo);
            if (correo != null && !CorreoFacturacionTienda.EsCorreoValido(correo))
            {
                return Invalido($"El correo «{correo}» no es válido: revíselo para mandar el enlace de pago.");
            }
            EnviosAgencia envio = await reparto.LeerEnvio(envioId).ConfigureAwait(false);
            if (envio == null || envio.Agencia != Constantes.Agencias.AGENCIA_REPARTO_PROPIO)
            {
                return ResultadoReparto<EnlaceTarjetaRepartoDTO>.Mal(TipoResultadoReparto.NoEncontrado, $"No existe el envío {envioId} del reparto propio.");
            }
            string empresa = envio.Empresa?.Trim();
            if (!AdmiteTarjeta(empresa))
            {
                return Invalido(string.Format(MENSAJE_EMPRESA_EFECTIVO, empresa));
            }
            if (envio.Estado == Constantes.Agencias.ESTADO_ENTREGADO)
            {
                return Conflicto($"El envío {envio.Numero} ya está entregado.");
            }
            if (envio.Estado != Constantes.Agencias.ESTADO_TRAMITADO && envio.Estado != Constantes.Agencias.ESTADO_INCIDENTADO)
            {
                return Conflicto($"El envío {envio.Numero} no está en reparto.");
            }
            decimal importe = Math.Round(envio.Reembolso, 2, MidpointRounding.AwayFromZero);
            if (importe <= 0)
            {
                return Invalido($"El envío {envio.Numero} no lleva reembolso: no hay nada que cobrar.");
            }
            string cliente = envio.Cliente?.Trim();
            string contacto = envio.Contacto?.Trim();
            DatosContactoCobro datos = await LeerContacto(empresa, cliente, contacto).ConfigureAwait(false);
            string nombre = Limpio(envio.Nombre) ?? datos.Nombre;
            var telefonos = new List<string> { envio.Movil, envio.Telefono };
            telefonos.AddRange(datos.Telefonos ?? new List<string>());
            string correoPropuesto = correo ?? CorreoValido(envio.Email) ?? CorreoValido(datos.Correo);
            string concepto = ServicioRepartoPropio.ConceptoEnlace(envio);
            string queSePaga = envio.Pedido.HasValue ? $"el pedido {envio.Pedido.Value} de Nueva Visión" : $"el envío {envio.Numero} de Nueva Visión";

            EnlaceTarjetaReparto previo = await repositorio.LeerUltimoDelEnvio(envio.Numero).ConfigureAwait(false);
            // Carlos 31/10/26: como el efectivo, el cobro con tarjeta LIQUIDA el efecto de la factura (copia su Nº documento, efecto…).
            // Se busca solo al crear un enlace nuevo (al reutilizar uno vivo sus efectos ya están en el PagoTPV: no se duplican).
            LiquidacionReembolsoTarjeta liquidacion = null;
            return await CrearOReutilizar(previo, importe, async () =>
            {
                liquidacion = await LiquidacionDelReembolso(envio, importe).ConfigureAwait(false);
                return new SolicitudPagoTPV
                {
                    Empresa = empresa,
                    Cliente = cliente,
                    Contacto = contacto,
                    Importe = importe,
                    Descripcion = concepto,
                    Correo = correo,
                    Vendedor = envio.Vendedor?.Trim(),
                    Efectos = liquidacion.Efecto == null ? null : new List<EfectoAPagar> { liquidacion.Efecto }
                };
            }, new EnlaceTarjetaReparto
            {
                Tipo = TiposCobroTarjetaReparto.REEMBOLSO,
                EnvioId = envio.Numero,
                Empresa = empresa,
                Cliente = cliente,
                Contacto = contacto,
                Importe = importe,
                Usuario = usuario,
                Dispositivo = Recortar(dispositivo, 100)
            }, correo, correoPropuesto, MovilesWhatsApp.Internacionales(telefonos, datos.Pais), url => TextoWhatsApp(nombre, queSePaga, importe, url, ahora()),
                pagoId => AnotarLiquidacion(envio, pagoId, importe, liquidacion, usuario))
                .ConfigureAwait(false);
        }

        /// <summary>
        /// El efecto que liquida el enlace del reembolso: el que elegiría el efectivo (TramitacionEnviosService), solo si su pendiente
        /// es justo el reembolso. Si no hay o no cuadra, el cobro sigue «a cuenta» (como antes): nunca se bloquea por esto.
        /// </summary>
        private async Task<LiquidacionReembolsoTarjeta> LiquidacionDelReembolso(EnviosAgencia envio, decimal importe)
        {
            EfectoCobroReparto movimiento;
            try
            {
                movimiento = await repositorio.LeerEfectoDelReembolso(envio, importe).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Cobro con tarjeta #773] No se ha podido buscar la factura del envío {envio.Numero} " +
                    $"(cliente {envio.Cliente?.Trim()}): el cobro queda a cuenta. {ex.Message}", ex));
                return new LiquidacionReembolsoTarjeta { MotivoACuenta = "no se ha podido buscar la factura pendiente" };
            }
            return EfectoALiquidar(movimiento, importe);
        }

        /// <summary>
        /// Pura (para los tests): el EfectoAPagar del enlace o, si no se liquida, el motivo. Se liquida solo si el pendiente del efecto
        /// es exactamente el reembolso (el efectivo, en cambio, liquida el elegido por lo cobrado aunque no cuadre).
        /// </summary>
        internal static LiquidacionReembolsoTarjeta EfectoALiquidar(EfectoCobroReparto movimiento, decimal importe)
        {
            if (movimiento == null)
            {
                return new LiquidacionReembolsoTarjeta { MotivoACuenta = "el cliente no tiene ninguna factura pendiente (¿todavía sin facturar?)" };
            }
            string documento = movimiento.Documento?.Trim();
            decimal pendiente = Math.Round(movimiento.ImportePendiente, 2, MidpointRounding.AwayFromZero);
            if (!AdmiteTarjeta(movimiento.Empresa))
            {
                return new LiquidacionReembolsoTarjeta { MotivoACuenta = $"la factura pendiente {documento} es de la empresa {movimiento.Empresa?.Trim()}" };
            }
            if (pendiente != importe)
            {
                return new LiquidacionReembolsoTarjeta
                {
                    MotivoACuenta = $"la factura pendiente {documento} es de {pendiente.ToString("N2", Espanol)} €, no de {importe.ToString("N2", Espanol)} €"
                };
            }
            return new LiquidacionReembolsoTarjeta
            {
                Efecto = new EfectoAPagar
                {
                    ExtractoClienteId = movimiento.Id,
                    Importe = importe,
                    Documento = documento,
                    Efecto = movimiento.Efecto?.Trim(),
                    Contacto = movimiento.Contacto?.Trim(),
                    Vendedor = movimiento.Vendedor?.Trim(),
                    FormaVenta = movimiento.FormaVenta?.Trim(),
                    Delegacion = movimiento.Delegacion?.Trim(),
                    TipoApunte = movimiento.TipoApunte?.Trim()
                }
            };
        }

        /// <summary>En la historia del envío (y en ELMAH si queda a cuenta), qué hará el enlace al pagarse. Si falla, no rompe nada.</summary>
        private async Task AnotarLiquidacion(EnviosAgencia envio, int pagoId, decimal importe, LiquidacionReembolsoTarjeta liquidacion, string usuario)
        {
            if (liquidacion == null)
            {
                return;
            }
            string texto = liquidacion.Efecto != null
                ? $"Reparto propio: enlace de pago con tarjeta {pagoId} de {importe.ToString("N2", Espanol)} €; al pagarse liquida la factura " +
                  $"{liquidacion.Efecto.Documento}{(string.IsNullOrEmpty(liquidacion.Efecto.Efecto) ? string.Empty : "/" + liquidacion.Efecto.Efecto)}"
                : $"Reparto propio: enlace de pago con tarjeta {pagoId} de {importe.ToString("N2", Espanol)} €; al pagarse queda A CUENTA del " +
                  $"cliente (sin liquidar ninguna factura): {liquidacion.MotivoACuenta}";
            if (liquidacion.Efecto == null)
            {
                ElmahHelper.Log(new Exception($"[Cobro con tarjeta #773] Envío {envio.Numero}, cliente {envio.Cliente?.Trim()}: {texto}. " +
                    "Habrá que liquidarlo a mano."));
            }
            try
            {
                await repositorio.AnotarHistoria(new EnvioHistoria
                {
                    NumeroEnvio = envio.Numero,
                    Campo = "Reembolso",
                    ValorAnterior = envio.Reembolso.ToString("N2", Espanol),
                    Observaciones = texto,
                    Usuario = Recortar(usuario, 50) ?? "NestoAPI",
                    FechaModificacion = ahora()
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Cobro con tarjeta #773] No se ha podido anotar en la historia del envío {envio.Numero}: {ex.Message}", ex));
            }
        }

        // ---------------------------------------------------------------- una deuda (#766)

        public async Task<ResultadoReparto<EnlaceTarjetaRepartoDTO>> EnlaceDeuda(PeticionEnlaceTarjetaDeudaDTO peticion, string usuario, string dispositivo)
        {
            if (!repositorio.ScriptAplicado())
            {
                return Invalido(MENSAJE_FALTA_SCRIPT);
            }
            if (peticion == null || string.IsNullOrWhiteSpace(peticion.Cliente))
            {
                return Invalido("Indique el cliente al que cobra.");
            }
            string correo = Limpio(peticion.Correo);
            if (correo != null && !CorreoFacturacionTienda.EsCorreoValido(correo))
            {
                return Invalido($"El correo «{correo}» no es válido: revíselo para mandar el enlace de pago.");
            }
            List<int> ids = (peticion.Efectos ?? new List<int>()).Where(i => i > 0).Distinct().ToList();
            if (ids.Count == 0)
            {
                return Invalido("Para cobrar con tarjeta marque los efectos que paga: el enlace es por su suma (lo que no sea de un efecto, en efectivo).");
            }
            if (ids.Count > MAXIMO_EFECTOS)
            {
                return Invalido($"Como mucho {MAXIMO_EFECTOS} efectos en un enlace de pago.");
            }
            string cliente = peticion.Cliente.Trim();
            List<EfectoCobroReparto> leidos = await repositorio.LeerEfectos(ids).ConfigureAwait(false) ?? new List<EfectoCobroReparto>();
            var efectos = new List<EfectoCobroReparto>();
            foreach (int id in ids)
            {
                EfectoCobroReparto efecto = leidos.FirstOrDefault(e => e.Id == id);
                if (efecto == null)
                {
                    return Invalido($"El efecto {id} no existe.");
                }
                string texto = $"{efecto.Documento?.Trim()}/{efecto.Efecto?.Trim()}";
                if (!string.Equals(efecto.Cliente?.Trim(), cliente, StringComparison.OrdinalIgnoreCase))
                {
                    return Invalido($"El efecto {texto} es del cliente {efecto.Cliente?.Trim()}, no del {cliente}.");
                }
                if (!AdmiteTarjeta(efecto.Empresa?.Trim()))
                {
                    return Invalido($"El efecto {texto} es de la empresa {efecto.Empresa?.Trim()}: se cobra siempre en efectivo.");
                }
                if (efecto.ImportePendiente <= 0 || efecto.Estado?.Trim() == Constantes.ExtractosCliente.Estados.DEUDA_VENCIDA)
                {
                    return Invalido($"El efecto {texto} no está pendiente de cobro: quítelo (con tarjeta solo se pagan efectos pendientes, sin abonos).");
                }
                efectos.Add(efecto);
            }
            decimal importe = Math.Round(efectos.Sum(e => e.ImportePendiente), 2, MidpointRounding.AwayFromZero);
            string contacto = Limpio(peticion.Contacto) ?? efectos[0].Contacto?.Trim() ?? "0";
            string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO;
            DatosContactoCobro datos = await LeerContacto(empresa, cliente, contacto).ConfigureAwait(false);
            string claveEfectos = ClaveEfectos(ids);
            List<string> documentos = efectos.Select(e => e.Documento?.Trim()).Where(d => !string.IsNullOrEmpty(d)).Distinct().ToList();
            string queSePaga = documentos.Count == 0 ? "sus recibos pendientes con Nueva Visión"
                : documentos.Count == 1 ? $"el documento {documentos[0]} de Nueva Visión"
                : $"los documentos {string.Join(", ", documentos.Take(5))}{(documentos.Count > 5 ? "…" : string.Empty)} de Nueva Visión";

            EnlaceTarjetaReparto previo = await repositorio.LeerUltimoDeDeuda(cliente, claveEfectos).ConfigureAwait(false);
            return await CrearOReutilizar(previo, importe, () => Task.FromResult(new SolicitudPagoTPV
            {
                Empresa = empresa,
                Cliente = cliente,
                Contacto = contacto,
                Importe = importe,
                Descripcion = documentos.Count == 1 ? $"Pago {documentos[0]} cliente {cliente}" : $"Pago efectos cliente {cliente}",
                Correo = correo,
                Vendedor = efectos[0].Vendedor?.Trim(),
                Efectos = efectos.Select(e => new EfectoAPagar
                {
                    ExtractoClienteId = e.Id,
                    Importe = Math.Round(e.ImportePendiente, 2, MidpointRounding.AwayFromZero),
                    Documento = e.Documento?.Trim(),
                    Efecto = e.Efecto?.Trim(),
                    Contacto = e.Contacto?.Trim(),
                    Vendedor = e.Vendedor?.Trim(),
                    FormaVenta = e.FormaVenta?.Trim(),
                    Delegacion = e.Delegacion?.Trim(),
                    TipoApunte = e.TipoApunte?.Trim()
                }).ToList()
            }), new EnlaceTarjetaReparto
            {
                Tipo = TiposCobroTarjetaReparto.DEUDA,
                Empresa = empresa,
                Cliente = cliente,
                Contacto = contacto,
                Importe = importe,
                Efectos = claveEfectos,
                Usuario = usuario,
                Dispositivo = Recortar(dispositivo, 100)
            }, correo, correo ?? CorreoValido(datos.Correo), MovilesWhatsApp.Internacionales(datos.Telefonos, datos.Pais),
                url => TextoWhatsApp(datos.Nombre, queSePaga, importe, url, ahora())).ConfigureAwait(false);
        }

        // ---------------------------------------------------------------- estado

        public async Task<ResultadoReparto<EstadoEnlaceTarjetaDTO>> Estado(int pagoTPVId)
        {
            if (!repositorio.ScriptAplicado())
            {
                return ResultadoReparto<EstadoEnlaceTarjetaDTO>.Mal(TipoResultadoReparto.Invalido, MENSAJE_FALTA_SCRIPT);
            }
            EnlaceTarjetaReparto enlace = await repositorio.LeerPorPago(pagoTPVId).ConfigureAwait(false);
            if (enlace == null)
            {
                return ResultadoReparto<EstadoEnlaceTarjetaDTO>.Mal(TipoResultadoReparto.NoEncontrado, $"El enlace {pagoTPVId} no es un cobro del reparto.");
            }
            List<PagoCadenaReparto> cadena = await repositorio.LeerCadena(enlace.PagoTPVId).ConfigureAwait(false);
            string estado = EstadoDeLaCadena(cadena);
            return ResultadoReparto<EstadoEnlaceTarjetaDTO>.Bien(new EstadoEnlaceTarjetaDTO
            {
                PagoTPVId = enlace.PagoTPVId,
                EnvioId = enlace.EnvioId,
                Cliente = enlace.Cliente?.Trim(),
                Tipo = enlace.Tipo?.Trim(),
                Importe = ImporteDeLaCadena(cadena) ?? enlace.Importe,
                Estado = estado,
                Pagado = estado == EstadosEnlaceTarjeta.PAGADO
            });
        }

        // ---------------------------------------------------------------- piezas

        /// <summary>Solo la empresa 1 cobra con tarjeta (Carlos 31/10/26: la 3, siempre en efectivo).</summary>
        public static bool AdmiteTarjeta(string empresa) => string.Equals(empresa?.Trim(), Constantes.Empresas.EMPRESA_POR_DEFECTO, StringComparison.Ordinal);

        /// <summary>El estado de la cadena: algún pago autorizado = pagado; el raíz anulado o devuelto = anulado; si no, el del último.</summary>
        public static string EstadoDeLaCadena(IEnumerable<PagoCadenaReparto> cadena)
        {
            List<PagoCadenaReparto> lista = (cadena ?? Enumerable.Empty<PagoCadenaReparto>()).Where(p => p != null).ToList();
            if (lista.Count == 0)
            {
                return EstadosEnlaceTarjeta.ANULADO;
            }
            if (lista.Any(p => Es(p.Estado, Constantes.EstadosPagoTPV.AUTORIZADO)))
            {
                return EstadosEnlaceTarjeta.PAGADO;
            }
            PagoCadenaReparto ultimo = lista.OrderByDescending(p => p.FechaCreacion).ThenByDescending(p => p.Id).First();
            if (lista.Any(p => Es(p.Estado, Constantes.EstadosPagoTPV.CANCELADO) || Es(p.Estado, Constantes.EstadosPagoTPV.DEVUELTO)))
            {
                return EstadosEnlaceTarjeta.ANULADO;
            }
            return Es(ultimo.Estado, Constantes.EstadosPagoTPV.DENEGADO) ? EstadosEnlaceTarjeta.DENEGADO : EstadosEnlaceTarjeta.PENDIENTE;
        }

        /// <summary>El importe pagado (o el del raíz si no se ha pagado); null sin cadena.</summary>
        internal static decimal? ImporteDeLaCadena(IEnumerable<PagoCadenaReparto> cadena)
        {
            List<PagoCadenaReparto> lista = (cadena ?? Enumerable.Empty<PagoCadenaReparto>()).Where(p => p != null).ToList();
            PagoCadenaReparto pagado = lista.FirstOrDefault(p => Es(p.Estado, Constantes.EstadosPagoTPV.AUTORIZADO));
            return pagado?.Importe ?? lista.OrderBy(p => p.Id).FirstOrDefault()?.Importe;
        }

        /// <summary>«123,456»: los efectos ordenados (para reconocer el mismo cobro).</summary>
        internal static string ClaveEfectos(IEnumerable<int> ids) => string.Join(",", ids.Distinct().OrderBy(i => i).Select(i => i.ToString(CultureInfo.InvariantCulture)));

        /// <summary>
        /// «Buenos días, PELUQUERÍA LA BONITA: para pagar con tarjeta el pedido 928000 de Nueva Visión (45,30 €) puede usar este
        /// enlace seguro: https://… Muchas gracias. Un saludo, Nueva Visión.» De usted y breve.
        /// </summary>
        public static string TextoWhatsApp(string nombre, string queSePaga, decimal importe, string url, DateTime momento)
        {
            string quien = string.IsNullOrWhiteSpace(nombre) ? string.Empty : ", " + nombre.Trim();
            return $"{GestorEnlacesDescarga.Saludo(momento)}{quien}: para pagar con tarjeta {queSePaga} " +
                   $"({importe.ToString("N2", Espanol)} €) puede usar este enlace seguro:\n{url}\n\nMuchas gracias. Un saludo,\nNueva Visión";
        }

        private async Task<ResultadoReparto<EnlaceTarjetaRepartoDTO>> CrearOReutilizar(EnlaceTarjetaReparto previo, decimal importe,
            Func<Task<SolicitudPagoTPV>> solicitud, EnlaceTarjetaReparto nuevo, string correoAMandar, string correoPropuesto, List<string> moviles,
            Func<string, string> texto, Func<int, Task> alCrear = null)
        {
            DateTime momento = ahora();
            if (previo != null)
            {
                List<PagoCadenaReparto> cadena = await repositorio.LeerCadena(previo.PagoTPVId).ConfigureAwait(false) ?? new List<PagoCadenaReparto>();
                string estado = EstadoDeLaCadena(cadena);
                PagoCadenaReparto raiz = cadena.FirstOrDefault(p => p.Id == previo.PagoTPVId);
                bool vivo = estado == EstadosEnlaceTarjeta.PENDIENTE || estado == EstadosEnlaceTarjeta.DENEGADO;
                bool reciente = previo.FechaCreacion >= momento.AddDays(-EnlacesPagoPedido.DIAS_ENLACE_PENDIENTE_VIVO);
                if (raiz != null && (estado == EstadosEnlaceTarjeta.PAGADO || (vivo && reciente && previo.Importe == importe)))
                {
                    // Un enlace vivo (o ya pagado) del mismo cobro: el mismo, nunca otro (no se duplican cobros). La página del raíz lleva
                    // a la regenerada si Redsys denegó el primero.
                    string urlPrevia = ServicioPagos.UrlPaginaPago(raiz.TokenAcceso);
                    bool enviado = false;
                    if (correoAMandar != null && estado != EstadosEnlaceTarjeta.PAGADO)
                    {
                        enviado = await repositorio.MandarPorCorreo(previo.PagoTPVId, correoAMandar).ConfigureAwait(false);
                    }
                    return ResultadoReparto<EnlaceTarjetaRepartoDTO>.Bien(Respuesta(previo, urlPrevia, ImporteDeLaCadena(cadena) ?? previo.Importe,
                        estado, true, enviado, correoPropuesto, moviles, texto));
                }
            }

            RespuestaIniciarPago pago;
            try
            {
                pago = await pagos().IniciarPago(await solicitud().ConfigureAwait(false), nuevo.Usuario).ConfigureAwait(false);
            }
            catch (ArgumentException ex)
            {
                return Invalido($"No se ha podido crear el enlace de pago: {ex.Message}");
            }
            nuevo.PagoTPVId = pago.IdPago;
            nuevo.FechaCreacion = momento;
            await repositorio.Guardar(nuevo).ConfigureAwait(false);
            if (alCrear != null)
            {
                await alCrear(pago.IdPago).ConfigureAwait(false);
            }
            return ResultadoReparto<EnlaceTarjetaRepartoDTO>.Bien(Respuesta(nuevo, pago.UrlPaginaPago, importe, EstadosEnlaceTarjeta.PENDIENTE, false,
                correoAMandar != null, correoPropuesto, moviles, texto));
        }

        private static EnlaceTarjetaRepartoDTO Respuesta(EnlaceTarjetaReparto enlace, string url, decimal importe, string estado, bool reutilizado,
            bool correoEnviado, string correo, List<string> moviles, Func<string, string> texto) => new EnlaceTarjetaRepartoDTO
            {
                PagoTPVId = enlace.PagoTPVId,
                EnvioId = enlace.EnvioId,
                Cliente = enlace.Cliente?.Trim(),
                Importe = importe,
                Url = url,
                Texto = texto(url),
                Moviles = moviles ?? new List<string>(),
                Correo = correo,
                Estado = estado,
                Reutilizado = reutilizado,
                CorreoEnviado = correoEnviado,
                GrupoAvisos = GrupoAvisos(enlace.PagoTPVId)
            };

        private async Task<DatosContactoCobro> LeerContacto(string empresa, string cliente, string contacto)
        {
            try
            {
                return await repositorio.LeerContacto(empresa, cliente, contacto).ConfigureAwait(false) ?? new DatosContactoCobro();
            }
            catch (Exception ex)
            {
                // Sin los teléfonos del cliente se manda igual (con el móvil del envío, o eligiendo el chat en WhatsApp)
                ElmahHelper.Log(new Exception($"[Cobro con tarjeta #773] No se han podido leer los teléfonos del cliente {cliente}: {ex.Message}", ex));
                return new DatosContactoCobro();
            }
        }

        private static bool Es(string estado, string valor) => string.Equals(estado?.Trim(), valor, StringComparison.OrdinalIgnoreCase);

        private static string CorreoValido(string correo)
        {
            string limpio = Limpio(correo);
            return limpio != null && CorreoFacturacionTienda.EsCorreoValido(limpio) ? limpio : null;
        }

        private static string Limpio(string valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

        private static string Recortar(string valor, int maximo) => valor == null || valor.Length <= maximo ? valor : valor.Substring(0, maximo);

        private static ResultadoReparto<EnlaceTarjetaRepartoDTO> Invalido(string mensaje)
            => ResultadoReparto<EnlaceTarjetaRepartoDTO>.Mal(TipoResultadoReparto.Invalido, mensaje);

        private static ResultadoReparto<EnlaceTarjetaRepartoDTO> Conflicto(string mensaje)
            => ResultadoReparto<EnlaceTarjetaRepartoDTO>.Mal(TipoResultadoReparto.Conflicto, mensaje);
    }

    /// <summary>
    /// NestoAPI#773: las tablas RepartoCobrosTarjeta (por SQL, fuera del EDMX, como las demás del reparto) y PagosTPV (por EF).
    /// </summary>
    public class RepositorioCobrosTarjetaReparto : IRepositorioCobrosTarjetaReparto
    {
        public const string TABLA = "RepartoCobrosTarjeta";
        private const string COLUMNAS = "Id, PagoTPVId, Tipo, EnvioId, Empresa, Cliente, Contacto, Importe, Efectos, Usuario, Dispositivo, FechaCreacion";

        private readonly NVEntities db;

        public RepositorioCobrosTarjetaReparto(NVEntities db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public bool ScriptAplicado() => ColumnasOpcionales.ExisteTabla(db, TABLA);

        public async Task<EnlaceTarjetaReparto> LeerUltimoDelEnvio(int envioId)
            => (await db.Database.SqlQuery<EnlaceTarjetaReparto>(
                $"SELECT TOP 1 {COLUMNAS} FROM dbo.{TABLA} WHERE EnvioId = @p0 AND Tipo = @p1 ORDER BY Id DESC",
                envioId, TiposCobroTarjetaReparto.REEMBOLSO).ToListAsync().ConfigureAwait(false)).FirstOrDefault();

        public async Task<EnlaceTarjetaReparto> LeerUltimoDeDeuda(string cliente, string efectos)
            => (await db.Database.SqlQuery<EnlaceTarjetaReparto>(
                $"SELECT TOP 1 {COLUMNAS} FROM dbo.{TABLA} WHERE Tipo = @p0 AND Cliente = @p1 AND Efectos = @p2 ORDER BY Id DESC",
                TiposCobroTarjetaReparto.DEUDA, cliente ?? string.Empty, efectos ?? string.Empty).ToListAsync().ConfigureAwait(false)).FirstOrDefault();

        public async Task<EnlaceTarjetaReparto> LeerPorPago(int pagoTPVId)
            => (await db.Database.SqlQuery<EnlaceTarjetaReparto>(
                $"SELECT TOP 1 {COLUMNAS} FROM dbo.{TABLA} WHERE PagoTPVId = @p0", pagoTPVId).ToListAsync().ConfigureAwait(false)).FirstOrDefault();

        public Task<List<PagoCadenaReparto>> LeerCadena(int pagoRaiz)
            => db.PagosTPV.AsNoTracking()
                .Where(p => p.Id == pagoRaiz || p.PagoOriginalId == pagoRaiz)
                .Select(p => new PagoCadenaReparto
                {
                    Id = p.Id,
                    PagoOriginalId = p.PagoOriginalId,
                    Estado = p.Estado,
                    Importe = p.Importe,
                    FechaCreacion = p.FechaCreacion,
                    TokenAcceso = p.TokenAcceso
                })
                .ToListAsync();

        public Task Guardar(EnlaceTarjetaReparto enlace)
            => db.Database.ExecuteSqlCommandAsync(
                $"INSERT INTO dbo.{TABLA} (PagoTPVId, Tipo, EnvioId, Empresa, Cliente, Contacto, Importe, Efectos, Usuario, Dispositivo, FechaCreacion) " +
                "VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10)",
                enlace.PagoTPVId, enlace.Tipo, (object)enlace.EnvioId ?? DBNull.Value, (object)enlace.Empresa ?? DBNull.Value,
                (object)enlace.Cliente ?? DBNull.Value, (object)enlace.Contacto ?? DBNull.Value, enlace.Importe, (object)enlace.Efectos ?? DBNull.Value,
                Recortar(enlace.Usuario, 50) ?? "NestoAPI", (object)Recortar(enlace.Dispositivo, 100) ?? DBNull.Value, enlace.FechaCreacion);

        public async Task<DatosContactoCobro> LeerContacto(string empresa, string cliente, string contacto)
        {
            var ficha = await db.Clientes.AsNoTracking()
                .Where(c => c.Empresa == empresa && c.Nº_Cliente == cliente && c.Contacto == contacto)
                .Select(c => new { c.Nombre, c.Teléfono, c.Pais })
                .FirstOrDefaultAsync().ConfigureAwait(false);
            var personas = await db.PersonasContactoClientes.AsNoTracking()
                .Where(p => p.Empresa == empresa && p.NºCliente == cliente && p.Contacto == contacto
                    && p.Estado >= Constantes.Clientes.PersonasContacto.ESTADO_POR_DEFECTO)
                .OrderBy(p => p.Número)
                .Select(p => new { p.Teléfono, p.CorreoElectrónico })
                .ToListAsync().ConfigureAwait(false);
            var datos = new DatosContactoCobro { Nombre = ficha?.Nombre?.Trim(), Pais = ficha?.Pais?.Trim() };
            if (!string.IsNullOrWhiteSpace(ficha?.Teléfono))
            {
                datos.Telefonos.Add(ficha.Teléfono);
            }
            datos.Telefonos.AddRange(personas.Select(p => p.Teléfono).Where(t => !string.IsNullOrWhiteSpace(t)));
            datos.Correo = personas.Select(p => p.CorreoElectrónico?.Trim())
                .FirstOrDefault(c => !string.IsNullOrEmpty(c) && CorreoFacturacionTienda.EsCorreoValido(c));
            return datos;
        }

        public Task<List<EfectoCobroReparto>> LeerEfectos(IReadOnlyCollection<int> ids) => new RepositorioCobrosReparto(db).LeerEfectos(ids);

        public async Task<EfectoCobroReparto> LeerEfectoDelReembolso(EnviosAgencia envio, decimal importe)
        {
            ExtractoCliente movimiento = await new TramitacionEnviosService(db).BuscarMovimientoALiquidarAsync(envio, importe).ConfigureAwait(false);
            return movimiento == null ? null : new EfectoCobroReparto
            {
                Id = movimiento.Nº_Orden,
                Empresa = movimiento.Empresa,
                Cliente = movimiento.Número,
                Contacto = movimiento.Contacto,
                TipoApunte = movimiento.TipoApunte,
                Documento = movimiento.Nº_Documento,
                Efecto = movimiento.Efecto,
                Importe = movimiento.Importe,
                ImportePendiente = movimiento.ImportePdte,
                Vencimiento = movimiento.FechaVto ?? movimiento.Fecha,
                Delegacion = movimiento.Delegación,
                FormaVenta = movimiento.FormaVenta,
                Ruta = movimiento.Ruta,
                Vendedor = movimiento.Vendedor,
                Estado = movimiento.Estado
            };
        }

        public async Task AnotarHistoria(EnvioHistoria fila)
        {
            _ = db.EnviosHistorias.Add(fila);
            _ = await db.SaveChangesAsync().ConfigureAwait(false);
        }

        public async Task<bool> MandarPorCorreo(int pagoTPVId, string correo)
        {
            PagoTPV pago = await db.PagosTPV.Include(p => p.PagosTPV_Efectos).FirstOrDefaultAsync(p => p.Id == pagoTPVId).ConfigureAwait(false);
            if (pago == null)
            {
                return false;
            }
            pago.Correo = correo;
            _ = await db.SaveChangesAsync().ConfigureAwait(false);
            List<EfectoAPagar> efectos = (pago.PagosTPV_Efectos ?? new List<PagoTPV_Efecto>()).Select(e => new EfectoAPagar
            {
                ExtractoClienteId = e.ExtractoClienteId,
                Importe = e.Importe,
                Documento = e.Documento
            }).ToList();
            new ServicioPagos(new RedsysService(), new Contabilidad.ContabilidadService(), new LectorParametrosUsuario())
                .EnviarCorreoPreCobro(pago, efectos, ServicioPagos.UrlPaginaPago(pago.TokenAcceso));
            return true;
        }

        private static string Recortar(string valor, int maximo) => valor == null || valor.Length <= maximo ? valor : valor.Substring(0, maximo);
    }
}
