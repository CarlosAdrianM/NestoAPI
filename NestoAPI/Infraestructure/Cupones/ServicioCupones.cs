using NestoAPI.Infraestructure.Eventos;
using NestoAPI.Infrastructure;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.Entity;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Cupones
{
    public interface IServicioCupones
    {
        /// <summary>Los del cliente. <paramref name="todos"/> false: solo los que se pueden usar o se podrán (Activo y Emitido).</summary>
        Task<List<CuponDTO>> LeerDelCliente(string empresa, string cliente, bool todos, IPrincipal usuario);
        Task<CuponDTO> LeerPorCodigo(string codigo, IPrincipal usuario);
        /// <summary>Solo Dirección, con motivo obligatorio; mismas reglas que los de evento.</summary>
        Task<CuponDTO> CrearManual(CrearCuponManualDTO peticion, IPrincipal usuario);
        /// <summary>Devolución del curso: anula el cupón si no se ha usado; si ya se usó, avisa (409) y no lo toca.</summary>
        Task<CuponDTO> Anular(string codigo, AnularCuponDTO peticion, IPrincipal usuario);
        /// <summary>
        /// NestoAPI#614: el cupón se ha gastado en la tienda PrestaShop. Con <see cref="UsarCuponTiendaDTO.Pedido"/>, lo llama Nesto
        /// al importar el pedido; sin él y con <see cref="UsarCuponTiendaDTO.PedidoCanal"/> (NestoAPI#694), el módulo nestocheques al
        /// validar el pedido en la tienda. <paramref name="conApiKey"/>: la petición trae la X-API-KEY de PrestaShop (sin JWT).
        /// </summary>
        Task<UsoCuponTiendaDTO> UsarEnTienda(string codigo, UsarCuponTiendaDTO peticion, IPrincipal usuario, bool conApiKey = false);
    }

    /// <summary>
    /// POST api/Cupones/{codigo}/Usar. NestoAPI#614: Nesto, al importar un pedido de PrestaShop con el cupón (con Pedido).
    /// NestoAPI#694: el módulo nestocheques de la tienda, al validar el pedido (sin Pedido, con PedidoCanal = referencia de la tienda).
    /// </summary>
    public class UsarCuponTiendaDTO
    {
        public string Empresa { get; set; }
        /// <summary>El pedido de Nesto, ya creado, con la línea del producto del cupón. 0 desde la tienda (NestoAPI#694): todavía no hay.</summary>
        public int Pedido { get; set; }
        /// <summary>El pedido de la tienda (id o referencia de PrestaShop), para el aviso. Es lo mismo que <see cref="PedidoCanal"/>.</summary>
        public string ReferenciaTienda { get; set; }
        /// <summary>El pedido de la tienda tal como lo manda Nesto#518 (= <see cref="ReferenciaTienda"/>).</summary>
        public string PedidoCanal { get; set; }
        /// <summary>Lo que descontó la tienda por el cupón (Nesto#518), para el aviso. Opcional.</summary>
        public decimal? ImporteDescontado { get; set; }
        /// <summary>Opcional: si no viene, «PrestaShop».</summary>
        public string Canal { get; set; }

        internal string Referencia => string.IsNullOrWhiteSpace(PedidoCanal) ? ReferenciaTienda?.Trim() : PedidoCanal.Trim();
    }

    public static class ResultadosUsoCuponTienda
    {
        /// <summary>Queda Usado en el pedido.</summary>
        public const string USADO = "Usado";
        /// <summary>Ya estaba Usado en ese pedido (la segunda llamada no hace nada).</summary>
        public const string YA_EN_ESTE_PEDIDO = "YaEnEstePedido";
        /// <summary>Ya estaba usado en otro pedido o anulado: el pedido entra igual y se avisa a Administración.</summary>
        public const string CONFLICTO = "Conflicto";
    }

    public class UsoCuponTiendaDTO
    {
        public string Resultado { get; set; }
        public string Mensaje { get; set; }
        /// <summary>Se ha mandado el correo a Administración.</summary>
        public bool AvisoAdministracion { get; set; }
        public CuponDTO Cupon { get; set; }
    }

    /// <summary>NestoAPI#613: POST api/Cupones/{codigo}/Anular.</summary>
    public class AnularCuponDTO
    {
        /// <summary>Opcional: si no viene, «Devolución del curso.».</summary>
        public string Motivo { get; set; }
    }

    /// <summary>
    /// NestoAPI#613: los cupones vistos desde fuera del pedido: consultar (la plantilla avisa «tiene un cupón de 50 €», como las
    /// ofertas), emitir a mano (Dirección), anular por no asistir (lista del corte 1) y el job diario de estados.
    /// </summary>
    public class ServicioCupones : IServicioCupones
    {
        public const string MOTIVO_NO_ASISTIO = "No asistió al evento.";
        public const string MOTIVO_DEVOLUCION = "Devolución del curso.";
        public const string ERROR_USADO_NO_SE_ANULA = "CUPON_USADO_NO_SE_ANULA";

        private readonly IRepositorioCupones repositorio;
        private readonly NVEntities db;
        private readonly Func<DateTime> ahora;
        private readonly string productoCupon;
        private readonly IEspejoCupones espejo;
        private readonly Func<IServicioCorreoElectronico> correo;
        private readonly Func<bool> usoEnTiendaEncendido;
        private readonly Func<Task<List<string>>> productosCheque;

        public ServicioCupones(NVEntities db) : this(new RepositorioCupones(db), db, null, null, new ServicioEspejoCuponesPrestashop(db),
            () => new ServicioCorreoElectronico(), () => ReglasEspejoPrestashop.Encendido,
            new ChequesRegalo.ServicioCanjeChequesRegalo(new ChequesRegalo.RepositorioCanjeChequesRegalo(db)).ProductosCheque)
        {
        }

        /// <param name="espejo">NestoAPI#614: llevar el cupón a PrestaShop al emitirlo, anularlo o usarlo en la tienda. Null: nada.</param>
        /// <param name="correo">NestoAPI#614: el aviso a Administración de un cupón gastado dos veces. Null: sin correo.</param>
        /// <param name="usoEnTiendaEncendido">NestoAPI#614: Cupones:EspejoPrestashop. Null: apagado.</param>
        /// <param name="productosCheque">NestoAPI#696: los productos de cheque regalo, para no dar por usado el cupón en un pedido que ya
        /// lleva el cheque (no son acumulables). Null: no se mira.</param>
        internal ServicioCupones(IRepositorioCupones repositorio, NVEntities db, Func<DateTime> ahora, string productoCupon,
            IEspejoCupones espejo = null, Func<IServicioCorreoElectronico> correo = null, Func<bool> usoEnTiendaEncendido = null,
            Func<Task<List<string>>> productosCheque = null)
        {
            this.repositorio = repositorio;
            this.db = db;
            this.ahora = ahora ?? (() => DateTime.Now);
            this.productoCupon = productoCupon ?? ReglasCupon.ProductoCupon;
            this.espejo = espejo ?? EspejoCuponesApagado.Instancia;
            this.correo = correo;
            this.usoEnTiendaEncendido = usoEnTiendaEncendido ?? (() => false);
            this.productosCheque = productosCheque;
        }

        public async Task<List<CuponDTO>> LeerDelCliente(string empresa, string cliente, bool todos, IPrincipal usuario)
        {
            string clienteLimpio = cliente?.Trim();
            if (string.IsNullOrEmpty(clienteLimpio))
            {
                throw new NestoBusinessException("Falta el cliente.");
            }
            string clienteDelToken = PermisosEventos.ClienteDelToken(usuario);
            if (clienteDelToken != null && clienteDelToken != clienteLimpio)
            {
                throw new NestoBusinessException("Solo puede ver sus cupones.") { StatusCode = HttpStatusCode.Forbidden };
            }
            DateTime hoy = ahora();
            List<CuponLeido> cupones = await repositorio.LeerDelCliente(string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim(),
                clienteLimpio).ConfigureAwait(false) ?? new List<CuponLeido>();
            List<CuponDTO> lista = ReglasCupon.Ordenar(cupones
                .Select(c => ReglasCupon.ADTO(c, hoy, productoCupon))
                .Where(c => todos || c.Estado == EstadosCupon.ACTIVO || c.Estado == EstadosCupon.EMITIDO));
            await CompletarGanavisiones(lista).ConfigureAwait(false);
            return lista;
        }

        /// <summary>NestoAPI#614 (Nesto#518): lo que resta cada cupón de la base de Ganavisiones, como el cheque regalo.</summary>
        private async Task CompletarGanavisiones(List<CuponDTO> cupones)
        {
            if (cupones == null || !cupones.Any())
            {
                return;
            }
            foreach (IGrouping<string, CuponDTO> porEmpresa in cupones.GroupBy(c => c.Empresa ?? Constantes.Empresas.EMPRESA_POR_DEFECTO))
            {
                Dictionary<string, ProductoParaCupon> productos = await repositorio.LeerProductos(porEmpresa.Key, new[] { productoCupon })
                    .ConfigureAwait(false);
                ProductoParaCupon producto = ReglasCupon.Buscar(productos, productoCupon);
                foreach (CuponDTO c in porEmpresa)
                {
                    c.RestaDeLaBaseGanavisiones = ReglasCupon.RestaDeLaBaseGanavisiones(c, producto);
                }
            }
        }

        public async Task<CuponDTO> LeerPorCodigo(string codigo, IPrincipal usuario)
        {
            CuponLeido cupon = await repositorio.LeerPorCodigo(codigo).ConfigureAwait(false)
                ?? throw new NestoBusinessException($"No existe el cupón {codigo?.Trim()}.") { StatusCode = HttpStatusCode.NotFound };
            string clienteDelToken = PermisosEventos.ClienteDelToken(usuario);
            if (clienteDelToken != null && clienteDelToken != cupon.Cliente?.Trim())
            {
                throw new NestoBusinessException($"No existe el cupón {codigo?.Trim()}.") { StatusCode = HttpStatusCode.NotFound };
            }
            CuponDTO dto = ReglasCupon.ADTO(cupon, ahora(), productoCupon);
            await CompletarGanavisiones(new List<CuponDTO> { dto }).ConfigureAwait(false);
            await CompletarUsoTienda(cupon, dto).ConfigureAwait(false);
            return dto;
        }

        /// <summary>
        /// NestoAPI#694: si se gastó en la tienda, la referencia de su pedido y lo descontado (CanalesExternos lo necesita para
        /// reconocer que el cupón ya está gastado en ESE pedido y no volver a canjearlo).
        /// </summary>
        private async Task CompletarUsoTienda(CuponLeido cupon, CuponDTO dto)
        {
            if (dto?.Estado != EstadosCupon.USADO || !ReglasCupon.GastadoEnLaTienda(cupon, cupon.UsadoEnPedido ?? 0))
            {
                return;
            }
            UsoTiendaCupon uso = await repositorio.LeerUsoTienda(cupon.Id).ConfigureAwait(false);
            dto.PedidoCanal = string.IsNullOrWhiteSpace(uso?.PedidoCanal) ? null : uso.PedidoCanal.Trim();
            dto.ImporteDescontado = uso?.ImporteDescontado;
        }

        public async Task<CuponDTO> CrearManual(CrearCuponManualDTO peticion, IPrincipal usuario)
        {
            if (usuario == null || PermisosEventos.ClienteDelToken(usuario) != null || !usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.DIRECCION))
            {
                throw new NestoBusinessException("Los cupones sin evento solo los puede emitir Dirección.") { StatusCode = HttpStatusCode.Forbidden };
            }
            if (peticion == null || string.IsNullOrWhiteSpace(peticion.Cliente))
            {
                throw new NestoBusinessException("Falta el cliente del cupón.");
            }
            string motivo = peticion.Motivo?.Trim();
            if (string.IsNullOrEmpty(motivo))
            {
                throw new NestoBusinessException("Indique el motivo del cupón: es obligatorio en los cupones sin evento.");
            }
            if (motivo.Length > 500)
            {
                throw new NestoBusinessException("El motivo no puede tener más de 500 caracteres.");
            }
            decimal importe = Math.Round(peticion.Importe, 2, MidpointRounding.AwayFromZero);
            if (importe <= 0)
            {
                throw new NestoBusinessException("El importe del cupón tiene que ser mayor que cero.");
            }
            string empresa = string.IsNullOrWhiteSpace(peticion.Empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : peticion.Empresa.Trim();
            string cliente = peticion.Cliente.Trim();
            string contacto = string.IsNullOrWhiteSpace(peticion.Contacto) ? null : peticion.Contacto.Trim();
            List<Cliente> fichas = await db.Clientes.Where(c => c.Empresa == empresa && c.Nº_Cliente == cliente).ToListAsync().ConfigureAwait(false);
            if (!fichas.Any() || (contacto != null && !fichas.Any(f => f.Contacto?.Trim() == contacto)))
            {
                throw new NestoBusinessException(contacto == null ? $"No existe el cliente {cliente}." : $"No existe el cliente {cliente}/{contacto}.")
                    { StatusCode = HttpStatusCode.NotFound };
            }
            DateTime hoy = ahora().Date;
            DateTime validoDesde = (peticion.ValidoDesde ?? hoy).Date;
            if (validoDesde < hoy)
            {
                throw new NestoBusinessException("El cupón no puede valer desde una fecha pasada.");
            }
            string usuarioAuditoria = UsuarioAuditoriaHelper.ParaAuditoria(UsuarioAuditoriaHelper.Resolver(usuario, null));
            CuponLeido creado = await repositorio.Insertar(new CuponNuevo
            {
                Empresa = empresa,
                Cliente = cliente,
                Contacto = contacto,
                Importe = importe,
                Origen = OrigenesCupon.MANUAL,
                Motivo = motivo,
                ValidoDesde = validoDesde,
                Caducidad = ReglasCupon.Caducidad(validoDesde),
                Estado = validoDesde > hoy ? EstadosCupon.EMITIDO : EstadosCupon.ACTIVO,
                Usuario = usuarioAuditoria
            }, () => ReglasCupon.NuevoCodigo(OrigenesCupon.MANUAL, hoy)).ConfigureAwait(false);
            if (creado != null)
            {
                await espejo.Sincronizar(creado.Id).ConfigureAwait(false);
            }
            return ReglasCupon.ADTO(creado, ahora(), productoCupon);
        }

        /// <summary>
        /// Decisión de Carlos (15/10/26) sobre la devolución de un curso o de una señal ya facturada: la factura se rectifica a
        /// mano, como hoy, y aquí se anula el cupón si no se ha usado. Si ya se usó, no se toca y se avisa (409 con el pedido y
        /// la factura donde se usó) para que se tenga en cuenta al rectificar. Administración y Dirección. Idempotente: un cupón
        /// ya anulado se devuelve tal cual.
        /// </summary>
        public async Task<CuponDTO> Anular(string codigo, AnularCuponDTO peticion, IPrincipal usuario)
        {
            if (usuario == null || PermisosEventos.ClienteDelToken(usuario) != null
                || !(usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.ADMINISTRACION) || usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.DIRECCION)))
            {
                throw new NestoBusinessException("Los cupones solo los pueden anular Administración y Dirección.") { StatusCode = HttpStatusCode.Forbidden };
            }
            CuponLeido cupon = await repositorio.LeerPorCodigo(codigo).ConfigureAwait(false)
                ?? throw new NestoBusinessException($"No existe el cupón {codigo?.Trim()}.") { StatusCode = HttpStatusCode.NotFound };
            string motivo = string.IsNullOrWhiteSpace(peticion?.Motivo) ? MOTIVO_DEVOLUCION : peticion.Motivo.Trim();
            if (motivo.Length > 200)
            {
                throw new NestoBusinessException("El motivo no puede tener más de 200 caracteres.");
            }
            DateTime hoy = ahora();
            string estado = ReglasCupon.EstadoEfectivo(cupon, hoy);
            if (estado == EstadosCupon.ANULADO)
            {
                return ReglasCupon.ADTO(cupon, hoy, productoCupon);
            }
            if (estado != EstadosCupon.USADO)
            {
                string usuarioAuditoria = UsuarioAuditoriaHelper.ParaAuditoria(UsuarioAuditoriaHelper.Resolver(usuario, null));
                if (await repositorio.Anular(cupon.Id, motivo, usuarioAuditoria).ConfigureAwait(false) > 0)
                {
                    await espejo.Sincronizar(cupon.Id).ConfigureAwait(false);
                    CuponLeido anulado = await repositorio.LeerPorId(cupon.Id).ConfigureAwait(false) ?? cupon;
                    return ReglasCupon.ADTO(anulado, ahora(), productoCupon);
                }
                // Se ha usado entre la lectura y el UPDATE: se vuelve a leer para avisar con el pedido
                cupon = await repositorio.LeerPorId(cupon.Id).ConfigureAwait(false) ?? cupon;
                if (ReglasCupon.EstadoEfectivo(cupon, hoy) == EstadosCupon.ANULADO)
                {
                    return ReglasCupon.ADTO(cupon, hoy, productoCupon);
                }
            }
            string factura = string.IsNullOrWhiteSpace(cupon.FacturaUso) ? string.Empty : $" (factura {cupon.FacturaUso.Trim()})";
            throw new NestoBusinessException(
                $"El cupón {cupon.Codigo?.Trim()} ya se ha usado en el pedido {cupon.UsadoEnPedido}{factura}: no se puede anular. " +
                $"Tenga en cuenta ese descuento de {ReglasCupon.Euros(cupon.Importe)} € al rectificar la factura del curso.",
                new ErrorContext { ErrorCode = ERROR_USADO_NO_SE_ANULA, Empresa = cupon.EmpresaUso?.Trim(), Pedido = cupon.UsadoEnPedido, Cliente = cupon.Cliente?.Trim() })
            { StatusCode = HttpStatusCode.Conflict };
        }

        /// <summary>
        /// NestoAPI#614: el cupón se ha gastado en la tienda PrestaShop. Nesto importa el pedido (CanalesExternos) con la línea del
        /// producto del cupón tal como la cobró la tienda y llama aquí con el pedido. Las reglas de Nesto (mínimo, fechas) no se miran:
        /// las aplicó la tienda y el cliente ya ha pagado. Si el cupón ya estaba usado en otro pedido o anulado (se gastó en los dos
        /// sitios a la vez), no se toca: el pedido entra igual y se avisa a Administración para resolverlo a mano. Lo puede llamar
        /// cualquiera de la casa (no el cliente de la tienda). Idempotente.
        /// </summary>
        public async Task<UsoCuponTiendaDTO> UsarEnTienda(string codigo, UsarCuponTiendaDTO peticion, IPrincipal usuario, bool conApiKey = false)
        {
            if (!conApiKey && (usuario == null || PermisosEventos.ClienteDelToken(usuario) != null))
            {
                throw new NestoBusinessException("Solo la casa puede marcar un cupón como usado en la tienda.") { StatusCode = HttpStatusCode.Forbidden };
            }
            if (peticion != null && peticion.Pedido <= 0 && !string.IsNullOrWhiteSpace(peticion.Referencia))
            {
                // NestoAPI#694: el módulo nestocheques al validar el pedido en la tienda. Sin el interruptor del espejo, que sigue apagado.
                return await UsarDesdeTienda(codigo, peticion, usuario, conApiKey).ConfigureAwait(false);
            }
            if (peticion == null || peticion.Pedido <= 0)
            {
                throw new NestoBusinessException("Indique el pedido de Nesto en el que se ha usado el cupón (o, desde la tienda, la referencia de su pedido en PedidoCanal).");
            }
            CuponLeido cupon = await repositorio.LeerPorCodigo(codigo).ConfigureAwait(false)
                ?? throw new NestoBusinessException($"No existe el cupón {codigo?.Trim()}.") { StatusCode = HttpStatusCode.NotFound };
            UsoCuponTiendaDTO yaGastadoEnLaTienda = await YaGastadoEnLaTiendaConEstaReferencia(cupon, peticion).ConfigureAwait(false);
            if (yaGastadoEnLaTienda != null)
            {
                return yaGastadoEnLaTienda;
            }
            if (!usoEnTiendaEncendido())
            {
                throw new NestoBusinessException($"El uso de los cupones en la tienda PrestaShop está apagado ({ReglasEspejoPrestashop.CLAVE_INTERRUPTOR}).");
            }
            string empresa = string.IsNullOrWhiteSpace(peticion.Empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : peticion.Empresa.Trim();
            string clientePedido = await repositorio.LeerClienteDelPedidoConLineaCupon(empresa, peticion.Pedido).ConfigureAwait(false);
            string canal = string.IsNullOrWhiteSpace(peticion.Canal) ? ReglasEspejoPrestashop.CANAL_PRESTASHOP : peticion.Canal.Trim();
            string codigoCupon = cupon.Codigo?.Trim();
            DateTime hoy = ahora();
            string referencia = string.IsNullOrWhiteSpace(peticion.Referencia) ? "" : $", {peticion.Referencia} de la tienda";
            string descontado = peticion.ImporteDescontado.HasValue ? $" con {ReglasCupon.Euros(peticion.ImporteDescontado.Value)} € de descuento" : "";

            if (clientePedido == null)
            {
                // Nesto#518: el pedido ha entrado sin la línea del cupón (no se ha podido reservar al crearlo: estaba usado o
                // anulado). Aquí no se toca el cupón: solo se avisa a Administración para que lo resuelva a mano.
                string estadoActual = ReglasCupon.EstadoEfectivo(cupon, hoy);
                string sinLinea = $"El cupón {codigoCupon} se ha gastado en la tienda (pedido {peticion.Pedido}{referencia}{descontado}) pero en Nesto " +
                    $"{MotivoNoLibre(cupon, estadoActual)}: el pedido ha entrado sin la línea del cupón. Hay que resolverlo a mano.";
                return new UsoCuponTiendaDTO
                {
                    Resultado = ResultadosUsoCuponTienda.CONFLICTO,
                    Mensaje = sinLinea,
                    AvisoAdministracion = AvisarAdministracion(cupon, peticion, empresa, null, sinLinea),
                    Cupon = ReglasCupon.ADTO(cupon, hoy, productoCupon)
                };
            }

            if (cupon.Estado?.Trim() == EstadosCupon.USADO && cupon.UsadoEnPedido == peticion.Pedido)
            {
                return new UsoCuponTiendaDTO
                {
                    Resultado = ResultadosUsoCuponTienda.YA_EN_ESTE_PEDIDO,
                    Mensaje = $"El cupón {codigoCupon} ya está usado en el pedido {peticion.Pedido}.",
                    Cupon = ReglasCupon.ADTO(cupon, hoy, productoCupon)
                };
            }

            // NestoAPI#696 (Carlos, 22/10/26): cheque regalo y cupón no son acumulables. Si el pedido ya lleva la línea del cheque, el
            // cupón NO se da por usado (sigue libre) y se avisa a Administración: la tienda lo descontó dos veces.
            if (await PedidoLlevaChequeRegalo(empresa, peticion.Pedido).ConfigureAwait(false))
            {
                string conCheque = $"{ReglasCupon.MensajeConChequeRegalo(codigoCupon)} El pedido {peticion.Pedido}{referencia}{descontado} ha " +
                    "entrado con la línea del cheque regalo y la del cupón: el cupón se deja libre y hay que quitar uno de los dos descuentos a mano.";
                return new UsoCuponTiendaDTO
                {
                    Resultado = ResultadosUsoCuponTienda.CONFLICTO,
                    Mensaje = conCheque,
                    AvisoAdministracion = AvisarAdministracion(cupon, peticion, empresa, clientePedido, conCheque),
                    Cupon = ReglasCupon.ADTO(cupon, hoy, productoCupon)
                };
            }

            string usuarioAuditoria = UsuarioAuditoriaHelper.ParaAuditoria(UsuarioAuditoriaHelper.Resolver(usuario, null));
            if (await repositorio.UsarEnTienda(cupon.Id, empresa, peticion.Pedido, canal, usuarioAuditoria).ConfigureAwait(false))
            {
                await espejo.Sincronizar(cupon.Id).ConfigureAwait(false);
                CuponLeido usado = await repositorio.LeerPorId(cupon.Id).ConfigureAwait(false) ?? cupon;
                bool otroCliente = !string.Equals(cupon.Cliente?.Trim(), clientePedido, StringComparison.OrdinalIgnoreCase);
                string mensaje = $"El cupón {codigoCupon} queda usado en el pedido {peticion.Pedido}.";
                bool avisado = false;
                if (otroCliente)
                {
                    mensaje += $" Atención: el cupón es del cliente {cupon.Cliente?.Trim()} y el pedido ha entrado con el cliente {clientePedido}.";
                    avisado = AvisarAdministracion(cupon, peticion, empresa, clientePedido,
                        $"El cupón {codigoCupon} es del cliente {cupon.Cliente?.Trim()} y el pedido {peticion.Pedido} de la tienda ha entrado con el " +
                        $"cliente {clientePedido} (por el NIF de la dirección). Se ha dado por usado en ese pedido: revise si el pedido es de quien tiene que ser.");
                }
                return new UsoCuponTiendaDTO
                {
                    Resultado = ResultadosUsoCuponTienda.USADO,
                    Mensaje = mensaje,
                    AvisoAdministracion = avisado,
                    Cupon = ReglasCupon.ADTO(usado, ahora(), productoCupon)
                };
            }

            // No se ha podido: usado en otro pedido o anulado. El pedido de la tienda entra igual; lo resuelve Administración.
            CuponLeido actual = await repositorio.LeerPorId(cupon.Id).ConfigureAwait(false) ?? cupon;
            string estado = ReglasCupon.EstadoEfectivo(actual, hoy);
            string conflicto = $"El cupón {codigoCupon} se ha gastado en la tienda (pedido {peticion.Pedido}{referencia}{descontado}) pero en Nesto " +
                $"{MotivoNoLibre(actual, estado)}: se ha descontado dos veces. El pedido entra igual; hay que resolverlo a mano.";
            bool aviso = AvisarAdministracion(actual, peticion, empresa, clientePedido, conflicto);
            return new UsoCuponTiendaDTO
            {
                Resultado = ResultadosUsoCuponTienda.CONFLICTO,
                Mensaje = conflicto,
                AvisoAdministracion = aviso,
                Cupon = ReglasCupon.ADTO(actual, hoy, productoCupon)
            };
        }

        /// <summary>
        /// NestoAPI#694: POST api/Cupones/{codigo}/Usar desde el módulo nestocheques, al validar el pedido en la tienda (sin pedido de
        /// Nesto: todavía no ha entrado). Queda Usado con el canal y la referencia de la tienda; cuando CanalesExternos importe el
        /// pedido con la línea del cupón, se ata a él sin volver a canjearlo. Idempotente: la misma referencia otra vez no hace nada.
        /// Si ya estaba gastado en otro sitio o anulado, el pedido de la tienda sigue adelante y se avisa a Administración.
        /// </summary>
        private async Task<UsoCuponTiendaDTO> UsarDesdeTienda(string codigo, UsarCuponTiendaDTO peticion, IPrincipal usuario, bool conApiKey)
        {
            CuponLeido cupon = await repositorio.LeerPorCodigo(codigo).ConfigureAwait(false)
                ?? throw new NestoBusinessException($"No existe el cupón {codigo?.Trim()}.") { StatusCode = HttpStatusCode.NotFound };
            string empresa = string.IsNullOrWhiteSpace(peticion.Empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : peticion.Empresa.Trim();
            string canal = string.IsNullOrWhiteSpace(peticion.Canal) || string.Equals(peticion.Canal.Trim(), ReglasCupon.CANAL_TIENDA, StringComparison.OrdinalIgnoreCase)
                ? ReglasCupon.CANAL_TIENDA
                : peticion.Canal.Trim();
            string referencia = peticion.Referencia;
            string codigoCupon = cupon.Codigo?.Trim();
            string usuarioAuditoria = conApiKey && usuario?.Identity?.IsAuthenticated != true
                ? CarritoCupones.USUARIO_TIENDA
                : UsuarioAuditoriaHelper.ParaAuditoria(UsuarioAuditoriaHelper.Resolver(usuario, null));

            ResultadoUsoDesdeTienda resultado = await repositorio.UsarDesdeTienda(cupon.Id, empresa, canal, referencia, peticion.ImporteDescontado,
                usuarioAuditoria).ConfigureAwait(false);
            switch (resultado)
            {
                case ResultadoUsoDesdeTienda.SinColumnas:
                    // 503: el módulo lo reintenta con su cron y la compra no se bloquea
                    throw new NestoBusinessException("Falta lanzar el script Issue694_CuponesTiendaPrestashop.sql: no se puede apuntar el cupón usado en la tienda.")
                        { StatusCode = HttpStatusCode.ServiceUnavailable };
                case ResultadoUsoDesdeTienda.YaConEstaReferencia:
                    return new UsoCuponTiendaDTO
                    {
                        Resultado = ResultadosUsoCuponTienda.YA_EN_ESTE_PEDIDO,
                        Mensaje = $"El cupón {codigoCupon} ya está usado en el pedido {referencia} de la tienda.",
                        Cupon = await DtoConUsoTienda(cupon.Id, cupon).ConfigureAwait(false)
                    };
                case ResultadoUsoDesdeTienda.Usado:
                    await espejo.Sincronizar(cupon.Id).ConfigureAwait(false);
                    string mensaje = $"El cupón {codigoCupon} queda usado en el pedido {referencia} de la tienda.";
                    if (peticion.ImporteDescontado.HasValue && Math.Abs(peticion.ImporteDescontado.Value - cupon.Importe) >= 0.01M)
                    {
                        mensaje += $" Atención: la tienda ha descontado {ReglasCupon.Euros(peticion.ImporteDescontado.Value)} € y el cupón es de " +
                            $"{ReglasCupon.Euros(cupon.Importe)} €.";
                        ElmahHelper.Log(new Exception($"[Cupones #694] {mensaje}"));
                    }
                    return new UsoCuponTiendaDTO
                    {
                        Resultado = ResultadosUsoCuponTienda.USADO,
                        Mensaje = mensaje,
                        Cupon = await DtoConUsoTienda(cupon.Id, cupon).ConfigureAwait(false)
                    };
                default:
                    CuponLeido actual = await repositorio.LeerPorId(cupon.Id).ConfigureAwait(false) ?? cupon;
                    DateTime hoy = ahora();
                    string estado = ReglasCupon.EstadoEfectivo(actual, hoy);
                    UsoTiendaCupon usoPrevio = ReglasCupon.UsadoEnLaTiendaSinPedido(actual)
                        ? await repositorio.LeerUsoTienda(actual.Id).ConfigureAwait(false)
                        : null;
                    string otraReferencia = string.IsNullOrWhiteSpace(usoPrevio?.PedidoCanal) ? "" : $" (pedido {usoPrevio.PedidoCanal.Trim()} de la tienda)";
                    string descontado = peticion.ImporteDescontado.HasValue ? $" con {ReglasCupon.Euros(peticion.ImporteDescontado.Value)} € de descuento" : "";
                    string conflicto = $"El cupón {codigoCupon} se ha gastado en la tienda (pedido {referencia}{descontado}) pero en Nesto " +
                        $"{MotivoNoLibre(actual, estado)}{otraReferencia}: se ha descontado dos veces. El pedido de la tienda sigue adelante; " +
                        "hay que resolverlo a mano.";
                    return new UsoCuponTiendaDTO
                    {
                        Resultado = ResultadosUsoCuponTienda.CONFLICTO,
                        Mensaje = conflicto,
                        AvisoAdministracion = AvisarAdministracion(actual, peticion, empresa, null, conflicto),
                        Cupon = ReglasCupon.ADTO(actual, hoy, productoCupon)
                    };
            }
        }

        /// <summary>
        /// NestoAPI#694: Nesto (CanalesExternos) llama a Usar con el pedido ya creado, pero el cupón ya lo apuntó el módulo de la tienda
        /// con la misma referencia: no es un conflicto ni hace falta el interruptor del espejo. Null si no es el caso.
        /// </summary>
        private async Task<UsoCuponTiendaDTO> YaGastadoEnLaTiendaConEstaReferencia(CuponLeido cupon, UsarCuponTiendaDTO peticion)
        {
            if (string.IsNullOrWhiteSpace(peticion.Referencia) || !ReglasCupon.GastadoEnLaTienda(cupon, peticion.Pedido))
            {
                return null;
            }
            UsoTiendaCupon uso = await repositorio.LeerUsoTienda(cupon.Id).ConfigureAwait(false);
            if (!string.Equals(uso?.PedidoCanal?.Trim(), peticion.Referencia, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            string pedidoNesto = cupon.UsadoEnPedido.HasValue ? $" (pedido {cupon.UsadoEnPedido} de Nesto)" : "";
            return new UsoCuponTiendaDTO
            {
                Resultado = ResultadosUsoCuponTienda.YA_EN_ESTE_PEDIDO,
                Mensaje = $"El cupón {cupon.Codigo?.Trim()} ya está usado en el pedido {peticion.Referencia} de la tienda{pedidoNesto}.",
                Cupon = await DtoConUsoTienda(cupon.Id, cupon).ConfigureAwait(false)
            };
        }

        private async Task<CuponDTO> DtoConUsoTienda(int id, CuponLeido porDefecto)
        {
            CuponLeido cupon = await repositorio.LeerPorId(id).ConfigureAwait(false) ?? porDefecto;
            CuponDTO dto = ReglasCupon.ADTO(cupon, ahora(), productoCupon);
            await CompletarUsoTienda(cupon, dto).ConfigureAwait(false);
            return dto;
        }

        /// <summary>NestoAPI#696: el pedido de Nesto tiene alguna línea de producto de cheque regalo. Sin productos de cheque, no.</summary>
        private async Task<bool> PedidoLlevaChequeRegalo(string empresa, int pedido)
        {
            if (productosCheque == null)
            {
                return false;
            }
            List<string> cheques = (await productosCheque().ConfigureAwait(false) ?? new List<string>())
                .Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return cheques.Any() && await repositorio.PedidoTieneLineaDe(empresa, pedido, cheques).ConfigureAwait(false);
        }

        private static string MotivoNoLibre(CuponLeido c, string estado) =>
            estado == EstadosCupon.USADO && !c.UsadoEnPedido.HasValue
                ? "ya se había usado en la tienda online"
            : estado == EstadosCupon.USADO
                ? $"ya estaba usado en el pedido {c.UsadoEnPedido}" + (string.IsNullOrWhiteSpace(c.FacturaUso) ? "" : $" (factura {c.FacturaUso.Trim()})")
                : estado == EstadosCupon.ANULADO
                    ? "estaba anulado" + (string.IsNullOrWhiteSpace(c.MotivoAnulacion) ? "" : $" ({c.MotivoAnulacion.Trim()})")
                    : $"estaba {estado?.ToLowerInvariant()}";

        private bool AvisarAdministracion(CuponLeido cupon, UsarCuponTiendaDTO peticion, string empresa, string clientePedido, string texto)
        {
            ElmahHelper.Log(new Exception($"[Cupones #614] {texto}"));
            if (correo == null)
            {
                return false;
            }
            try
            {
                using (MailMessage mail = CorreoUsoTienda(cupon, peticion, empresa, clientePedido, texto))
                {
                    return correo().EnviarCorreoSMTP(mail);
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Cupones #614] No se ha podido avisar a Administración del cupón {cupon.Codigo?.Trim()}: {ex.Message}", ex));
                return false;
            }
        }

        internal static MailMessage CorreoUsoTienda(CuponLeido cupon, UsarCuponTiendaDTO peticion, string empresa, string clientePedido, string texto)
        {
            string Html(string t) => WebUtility.HtmlEncode(t ?? string.Empty);
            var cuerpo = new StringBuilder();
            cuerpo.Append("<!DOCTYPE html><html><head><meta charset='utf-8'></head><body style='font-family: Arial, sans-serif; font-size: 14px;'>");
            cuerpo.Append($"<p>{Html(texto)}</p><ul>");
            cuerpo.Append($"<li>Cupón: <b>{Html(cupon.Codigo?.Trim())}</b> de {ReglasCupon.Euros(cupon.Importe)} €, del cliente {Html(cupon.Cliente?.Trim())}" +
                (string.IsNullOrWhiteSpace(cupon.Evento) ? "" : $" («{Html(cupon.Evento.Trim())}»)") + "</li>");
            if (peticion.Pedido <= 0)
            {
                // NestoAPI#694: desde el módulo de la tienda, el pedido todavía no ha entrado en Nesto
                cuerpo.Append($"<li>Pedido de la tienda: referencia {Html(peticion.Referencia)} (todavía no ha entrado en Nesto)" +
                    (peticion.ImporteDescontado.HasValue ? $", descontado {ReglasCupon.Euros(peticion.ImporteDescontado.Value)} €" : "") + "</li>");
            }
            else
            {
                cuerpo.Append($"<li>Pedido de la tienda: {Html(empresa)}/{peticion.Pedido}" +
                    (string.IsNullOrWhiteSpace(clientePedido) ? " (sin la línea del cupón)" : $", cliente {Html(clientePedido)}") +
                    (string.IsNullOrWhiteSpace(peticion.Referencia) ? "" : $", referencia {Html(peticion.Referencia)}") +
                    (peticion.ImporteDescontado.HasValue ? $", descontado {ReglasCupon.Euros(peticion.ImporteDescontado.Value)} €" : "") + "</li>");
            }
            if (cupon.UsadoEnPedido.HasValue && cupon.UsadoEnPedido != peticion.Pedido)
            {
                cuerpo.Append($"<li>Donde ya se había usado: pedido {cupon.UsadoEnPedido}" +
                    (string.IsNullOrWhiteSpace(cupon.FacturaUso) ? "" : $", factura {Html(cupon.FacturaUso.Trim())}") + "</li>");
            }
            cuerpo.Append("</ul></body></html>");
            var mail = new MailMessage
            {
                From = new MailAddress(Constantes.Correos.TIENDA_ONLINE, "NestoAPI"),
                Subject = $"Cupón {cupon.Codigo?.Trim()} de la tienda PrestaShop: revisar",
                Body = cuerpo.ToString(),
                IsBodyHtml = true,
                BodyEncoding = Encoding.UTF8,
                SubjectEncoding = Encoding.UTF8
            };
            mail.To.Add(Constantes.Correos.CORREO_ADMON);
            mail.CC.Add(Constantes.Correos.TIENDA_ONLINE);
            return mail;
        }

        /// <summary>
        /// La lista del corte 1: quien no vino pierde el cupón (Anulado). Si se corrige la lista y sí vino, el cupón anulado por no
        /// asistir vuelve. Un cupón ya usado no se toca (se avisa en ELMAH para que lo mire Administración).
        /// </summary>
        public async Task AplicarAsistencia(IEnumerable<KeyValuePair<int, bool>> asistencias, string usuario)
        {
            foreach (KeyValuePair<int, bool> a in asistencias ?? Enumerable.Empty<KeyValuePair<int, bool>>())
            {
                if (a.Value)
                {
                    _ = await repositorio.ReactivarDeInscripcion(a.Key, MOTIVO_NO_ASISTIO, usuario).ConfigureAwait(false);
                    continue;
                }
                int anulados = await repositorio.AnularDeInscripcion(a.Key, MOTIVO_NO_ASISTIO, usuario).ConfigureAwait(false);
                if (anulados == 0)
                {
                    CuponLeido cupon = await repositorio.LeerDeInscripcion(a.Key).ConfigureAwait(false);
                    if (cupon?.Estado?.Trim() == EstadosCupon.USADO)
                    {
                        ElmahHelper.Log(new Exception($"[Cupones #613] La inscripción {a.Key} está como «No asistió», pero su cupón {cupon.Codigo?.Trim()} " +
                            $"ya está usado en el pedido {cupon.UsadoEnPedido}: no se anula. Decidan si se quita del pedido."));
                    }
                }
            }
        }

        /// <summary>Job diario: Emitido → Activo el día del evento; Activo → Caducado a los 6 meses.</summary>
        public Task<int> ActualizarEstados() => repositorio.ActualizarEstados();
    }

    /// <summary>
    /// NestoAPI#613: emite el cupón al confirmarse la señal (corte 2): importe = señal IVA incluido, válido desde el día del
    /// evento y 6 meses. Idempotente (un cupón por inscripción). Si la lista ya está pasada y no vino, no se emite. Avisos: push
    /// al vendedor (NestoApp) y, con Cupones:CorreoCliente encendido, correo al cliente con el código y las normas.
    /// </summary>
    public class EmisorCuponesEvento : IEmisorCuponEvento
    {
        public const string CLAVE_CORREO_CLIENTE = "Cupones:CorreoCliente";
        public const string CLAVE_URL_NORMAS = "Cupones:UrlNormas";
        public const string TIPO_NOTIFICACION = "CuponEmitido";

        private readonly IRepositorioCupones repositorio;
        private readonly IAvisadorCupones avisador;
        private readonly Func<DateTime> ahora;
        private readonly IEspejoCupones espejo;

        public EmisorCuponesEvento(NVEntities db)
            : this(new RepositorioCupones(db), new AvisadorCupones(db), null, new ServicioEspejoCuponesPrestashop(db))
        {
        }

        /// <param name="espejo">NestoAPI#614: llevarlo a PrestaShop al emitirlo (si falla, el job). Null: nada.</param>
        internal EmisorCuponesEvento(IRepositorioCupones repositorio, IAvisadorCupones avisador, Func<DateTime> ahora, IEspejoCupones espejo = null)
        {
            this.repositorio = repositorio;
            this.avisador = avisador;
            this.ahora = ahora ?? (() => DateTime.Now);
            this.espejo = espejo ?? EspejoCuponesApagado.Instancia;
        }

        public async Task Emitir(EventoInscripcion inscripcion, Evento evento, FacturacionCursoInscripcion facturacion, string usuario)
        {
            // NestoAPI#690: solo la señal reembolsable gana el cupón (la no reembolsable y la gratuita, no)
            if (inscripcion == null || evento == null || evento.ImporteSenal <= 0 || !ReglasModalidadEvento.EmiteCupon(inscripcion))
            {
                return;
            }
            if (ReglasInscripcionEvento.Estado(inscripcion.Estado) == EstadoInscripcionEvento.NoAsistio)
            {
                return; // pagó tarde y ya consta que no vino: no hay cupón
            }
            if (await repositorio.LeerDeInscripcion(inscripcion.Id).ConfigureAwait(false) != null)
            {
                return;
            }
            DateTime hoy = ahora().Date;
            DateTime validoDesde = evento.Fecha.Date;
            // NestoAPI#613 (Carlos, 29/10/26): el pedido mínimo lo define el evento; sin informar, 0 (cualquier importe)
            decimal pedidoMinimo = await repositorio.LeerPedidoMinimoCuponEvento(evento.Id).ConfigureAwait(false) ?? 0;
            CuponLeido cupon = await repositorio.Insertar(new CuponNuevo
            {
                Empresa = inscripcion.Empresa?.Trim() ?? Constantes.Empresas.EMPRESA_POR_DEFECTO,
                Cliente = inscripcion.Cliente?.Trim(),
                Contacto = inscripcion.Contacto?.Trim(),
                // La señal IVA incluido, nunca señal + IVA (Carlos, 08/10/26). Lo cobrado coincide con la señal (corte 2).
                Importe = Math.Round(evento.ImporteSenal, 2, MidpointRounding.AwayFromZero),
                Origen = OrigenesCupon.EVENTO,
                EventoInscripcionId = inscripcion.Id,
                EventoId = evento.Id,
                EmpresaFacturaOrigen = facturacion?.EmpresaFactura?.Trim(),
                FacturaOrigen = facturacion?.Factura?.Trim(),
                ValidoDesde = validoDesde,
                Caducidad = ReglasCupon.Caducidad(validoDesde),
                Estado = validoDesde > hoy ? EstadosCupon.EMITIDO : EstadosCupon.ACTIVO,
                Usuario = usuario,
                PedidoMinimo = Math.Max(0, Math.Round(pedidoMinimo, 2, MidpointRounding.AwayFromZero))
            }, () => ReglasCupon.NuevoCodigo(OrigenesCupon.EVENTO, validoDesde)).ConfigureAwait(false);
            if (cupon == null)
            {
                return;
            }
            await espejo.Sincronizar(cupon.Id).ConfigureAwait(false);
            await avisador.AvisarEmitido(cupon, inscripcion).ConfigureAwait(false);
        }
    }

    /// <summary>Avisos de un cupón recién emitido. Nunca lanza.</summary>
    public interface IAvisadorCupones
    {
        Task AvisarEmitido(CuponLeido cupon, EventoInscripcion inscripcion);
    }

    public class AvisadorCupones : IAvisadorCupones
    {
        private readonly NVEntities db;
        private readonly Func<IServicioNotificacionesPush> push;
        private readonly Func<IServicioCorreoElectronico> correo;
        private readonly Func<bool> correoEncendido;
        private readonly IRepositorioCupones repositorio;
        private static readonly CultureInfo es = CultureInfo.GetCultureInfo("es-ES");

        public AvisadorCupones(NVEntities db) : this(db, () => new ServicioNotificacionesPush(), () => new ServicioCorreoElectronico(),
            () => string.Equals(ConfigurationManager.AppSettings[EmisorCuponesEvento.CLAVE_CORREO_CLIENTE], "true", StringComparison.OrdinalIgnoreCase),
            new RepositorioCupones(db))
        {
        }

        internal AvisadorCupones(NVEntities db, Func<IServicioNotificacionesPush> push, Func<IServicioCorreoElectronico> correo, Func<bool> correoEncendido,
            IRepositorioCupones repositorio)
        {
            this.db = db;
            this.push = push;
            this.correo = correo;
            this.correoEncendido = correoEncendido;
            this.repositorio = repositorio;
        }

        public async Task AvisarEmitido(CuponLeido cupon, EventoInscripcion inscripcion)
        {
            string empresa = cupon.Empresa?.Trim() ?? Constantes.Empresas.EMPRESA_POR_DEFECTO;
            string cliente = cupon.Cliente?.Trim();
            Cliente ficha = null;
            try
            {
                ficha = (await db.Clientes.Where(c => c.Empresa == empresa && c.Nº_Cliente == cliente).ToListAsync().ConfigureAwait(false))
                    .FirstOrDefault(c => c.Contacto?.Trim() == cupon.Contacto?.Trim());
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Cupones #613] No se ha podido leer la ficha del cliente {cliente} para avisar del cupón: {ex.Message}", ex));
            }

            // Push al vendedor del cliente (NestoApp)
            try
            {
                string vendedor = ficha?.Vendedor?.Trim();
                if (!string.IsNullOrEmpty(vendedor))
                {
                    _ = await push().EnviarAVendedor(empresa, vendedor, Notificacion(cupon, ficha?.Nombre?.Trim())).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Cupones #613] No se ha podido avisar al vendedor del cupón {cupon.Codigo?.Trim()}: {ex.Message}", ex));
            }

            // Correo al cliente, detrás de su interruptor (el texto y las normas los tiene que ver Carlos antes)
            if (!correoEncendido())
            {
                return;
            }
            try
            {
                string destino = await CorreoDelCliente(empresa, cliente, cupon.Contacto?.Trim()).ConfigureAwait(false);
                if (destino == null)
                {
                    await repositorio.MarcarAviso(cupon.Id, "(sin correo)").ConfigureAwait(false);
                    return;
                }
                using (MailMessage mail = Correo(cupon, ficha?.Nombre?.Trim(), destino))
                {
                    bool enviado = correo().EnviarCorreoSMTP(mail);
                    await repositorio.MarcarAviso(cupon.Id, enviado ? destino : "(falló el envío)").ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Cupones #613] No se ha podido mandar al cliente {cliente} el correo del cupón {cupon.Codigo?.Trim()}: {ex.Message}", ex));
            }
        }

        internal static NotificacionPushDTO Notificacion(CuponLeido cupon, string nombre)
        {
            string quien = string.IsNullOrWhiteSpace(nombre) ? $"El cliente {cupon.Cliente?.Trim()}" : $"{nombre} ({cupon.Cliente?.Trim()})";
            return new NotificacionPushDTO
            {
                Titulo = $"Cupón de {ReglasCupon.Euros(cupon.Importe)} €",
                Cuerpo = $"{quien} tiene el cupón {cupon.Codigo?.Trim()}" + (string.IsNullOrWhiteSpace(cupon.Evento) ? "" : $" por «{cupon.Evento.Trim()}»") +
                    $": vale desde el {ReglasCupon.Fecha(cupon.ValidoDesde)} hasta el {ReglasCupon.Fecha(cupon.Caducidad)} en un pedido " +
                    $"{ReglasCupon.TextoPedidoMinimo(ReglasCupon.PedidoMinimo(cupon))}.",
                Tipo = EmisorCuponesEvento.TIPO_NOTIFICACION,
                Datos = new Dictionary<string, string>
                {
                    ["tipo"] = EmisorCuponesEvento.TIPO_NOTIFICACION,
                    ["empresa"] = cupon.Empresa?.Trim(),
                    ["cliente"] = cupon.Cliente?.Trim(),
                    ["contacto"] = cupon.Contacto?.Trim(),
                    ["codigo"] = cupon.Codigo?.Trim(),
                    ["importe"] = cupon.Importe.ToString(CultureInfo.InvariantCulture)
                }
            };
        }

        /// <summary>Los correos de las facturas (cargo 22) del contacto, como el cheque regalo.</summary>
        private async Task<string> CorreoDelCliente(string empresa, string cliente, string contacto)
        {
            List<string> correos = (await db.PersonasContactoClientes
                .Where(p => p.Empresa == empresa && p.NºCliente == cliente && p.Cargo == Constantes.Clientes.PersonasContacto.CARGO_FACTURA_POR_CORREO
                    && p.CorreoElectrónico != null)
                .Select(p => new { p.Contacto, p.CorreoElectrónico }).ToListAsync().ConfigureAwait(false))
                .OrderBy(p => p.Contacto?.Trim() == contacto ? 0 : 1)
                .Select(p => p.CorreoElectrónico.Trim()).Where(c => c.Length > 0).Distinct().ToList();
            return correos.FirstOrDefault();
        }

        internal static MailMessage Correo(CuponLeido cupon, string nombre, string destino)
        {
            string Html(string t) => WebUtility.HtmlEncode(t ?? string.Empty);
            string urlNormas = ConfigurationManager.AppSettings[EmisorCuponesEvento.CLAVE_URL_NORMAS]?.Trim();
            var cuerpo = new StringBuilder();
            cuerpo.Append("<!DOCTYPE html><html><head><meta charset='utf-8'></head><body style='font-family: Arial, sans-serif; font-size: 14px; max-width: 600px;'>");
            cuerpo.Append($"<p>Hola{(string.IsNullOrWhiteSpace(nombre) ? "" : ", " + Html(nombre))}:</p>");
            cuerpo.Append($"<p>Gracias por apuntarse a «{Html(cupon.Evento?.Trim())}». Tiene un cupón de <b>{ReglasCupon.Euros(cupon.Importe)} €</b> " +
                "de descuento para su próxima compra de productos:</p>");
            cuerpo.Append($"<p style='font-size: 22px; font-weight: bold; letter-spacing: 2px;'>{Html(cupon.Codigo?.Trim())}</p>");
            cuerpo.Append("<ul>");
            cuerpo.Append($"<li>Vale desde el {ReglasCupon.Fecha(cupon.ValidoDesde)} hasta el {ReglasCupon.Fecha(cupon.Caducidad)}.</li>");
            decimal minimo = ReglasCupon.PedidoMinimo(cupon);
            cuerpo.Append(minimo > 0
                ? $"<li>Se usa una sola vez, en un pedido de al menos {ReglasCupon.Euros(minimo)} € de productos " +
                    "(IVA incluido; no cuentan cursos ni portes). Uno por pedido.</li>"
                : "<li>Se usa una sola vez, en un pedido de productos (no vale para cursos ni portes). Uno por pedido.</li>");
            cuerpo.Append("<li>Si no asiste al evento, el cupón se pierde.</li>");
            cuerpo.Append("</ul>");
            if (!string.IsNullOrEmpty(urlNormas))
            {
                cuerpo.Append($"<p><a href='{Html(urlNormas)}'>Normas completas de los cupones</a></p>");
            }
            cuerpo.Append("<p>Nueva Visión</p></body></html>");
            var mail = new MailMessage
            {
                From = new MailAddress("nuevavision@nuevavision.es", "Nueva Visión"),
                Subject = $"Su cupón de {ReglasCupon.Euros(cupon.Importe)} € para su próxima compra",
                Body = cuerpo.ToString(),
                IsBodyHtml = true,
                BodyEncoding = Encoding.UTF8,
                SubjectEncoding = Encoding.UTF8
            };
            mail.To.Add(destino);
            return mail;
        }
    }
}
