using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infrastructure;
using NestoAPI.Models;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PreparacionAlmacen
{
    /// <summary>Una fila de evidencia de la recepción en PreparacionEscaneos (TipoOrigen COMP, Fase RECE).</summary>
    public class EvidenciaRecepcion
    {
        public Guid IdCliente { get; set; }
        /// <summary>El pedido de compra al que se asignó primero el producto; 0 si no estaba pedido.</summary>
        public int NumeroOrigen { get; set; }
        public string Producto { get; set; }
        public int Cantidad { get; set; }
        public string Usuario { get; set; }
        public string Dispositivo { get; set; }
        /// <summary>Para encontrar todas las filas de la misma recepción.</summary>
        public Guid IdRecepcion { get; set; }
        /// <summary>NestoAPI#673: de una recepción parcial (Motivo «Recepción parcial …»).</summary>
        public bool Parcial { get; set; }
    }

    /// <summary>Lo que se escribe al terminar una recepción de compras, todo dentro de una transacción.</summary>
    public interface ITransaccionRecepcionCompra
    {
        Task<bool> YaRegistrada(string empresa, IEnumerable<Guid> idsEvidencia);
        /// <summary>Las líneas de producto pendientes (estado 1) del proveedor en el almacén, bloqueadas hasta el final.</summary>
        Task<List<LineaCompraPendiente>> LeerLineasBloqueando(string empresa, string almacen, string proveedor);
        /// <summary>Lo recibido queda en la línea con fecha de hoy; si falta algo, va en una línea nueva (Resto).</summary>
        Task RecibirLinea(string empresa, LineaCompraPendiente linea, LineaRecibida recibida, DateTime hoy, string usuario);
        Task Anular(string empresa, int numeroOrden, string usuario);
        /// <summary>Una línea -99 vuelve a estado 1 para recibirse con su pedido (el paso inverso de prdDeshacerAlbaránCmp).</summary>
        Task Reactivar(string empresa, int numeroOrden, string usuario);
        Task CrearExceso(string empresa, LineaCompraPendiente copiaDe, ExcesoRecepcion exceso, DateTime hoy, string usuario);
        /// <summary>Cambia la fecha de recepción de líneas en curso (lo que hace prdInsertarLineaCmp con el resto del pedido).</summary>
        Task Aplazar(string empresa, IEnumerable<int> numerosOrden, DateTime fechaRecepcion);
        /// <summary>prdCrearAlbaránCmp por su único punto de llamada (PedidosCompraService).</summary>
        Task<int> CrearAlbaran(int pedido, string usuario);
        Task RegistrarEvidencia(string empresa, IEnumerable<EvidenciaRecepcion> filas);
        /// <summary>
        /// Ariadna#51 (corte 2): lo leído en curso con ese IdRecepcion pasa a Terminada, en esta transacción; con
        /// <paramref name="abrirOtra"/> (recibida en parte) se abre otro IdRecepcion para lo que queda. 0 si el IdRecepcion no era
        /// el compartido o la tabla no existe.
        /// </summary>
        Task<int> TerminarLecturasEnCurso(ClaveRecepcionEnCurso clave, Guid idRecepcion, bool abrirOtra, string usuario, string dispositivo);
        /// <summary>Para el ensayo: cómo leer las filas que toca terminar (antes y después), con los pedidos fijados antes.</summary>
        Task<Func<Task<List<FilaEnsayoDTO>>>> PrepararFoto(string empresa, IReadOnlyCollection<int> pedidos);
    }

    /// <summary>Avisar a los de Compras (buzón y campana de Nesto). Nunca debe romper la recepción.</summary>
    public interface IAvisadorCompras
    {
        Task Avisar(string titulo, IEnumerable<string> avisos);
        /// <summary>Lo mismo con otro tipo de notificación (p. ej. una falta del packing, que no es una recepción).</summary>
        Task Avisar(string titulo, IEnumerable<string> avisos, string tipo);
    }

    /// <summary>
    /// NestoAPI#559: recibir lo que llega de un proveedor. El documento es el PROVEEDOR: lo leído se reparte entre
    /// sus pedidos abiertos del más antiguo al más reciente (<see cref="PlanificadorRecepcionCompra"/>). Terminar,
    /// en una transacción: partir lo recibido en parte, -99 a lo que no llega si el proveedor no lleva control de
    /// pendientes, el exceso con o sin visto bueno según quien recibe sea de Compras, y el albarán de cada pedido.
    /// </summary>
    public class OrigenRecepcionCompras : IOrigenRecepcion
    {
        public const string TIPO = "COMP";

        private static readonly string[] gruposQuePuedenTerminar =
        {
            Constantes.GruposSeguridad.ALMACEN,
            Constantes.GruposSeguridad.COMPRAS,
            Constantes.GruposSeguridad.DIRECCION
        };

        private readonly IRepositorioRecepcionCompras repositorio;
        private readonly IAvisadorCompras avisador;
        private readonly Func<DateTime> hoy;

        public OrigenRecepcionCompras(IRepositorioRecepcionCompras repositorio, IAvisadorCompras avisador, Func<DateTime> hoy = null)
        {
            this.repositorio = repositorio;
            this.avisador = avisador;
            this.hoy = hoy ?? (() => DateTime.Today);
        }

        public string Tipo => TIPO;
        public bool SeTerminaDesdeAqui => true;
        public TextosTipoRecepcion Textos { get; } = new TextosCompras();

        /// <summary>NestoAPI#600: lo que se le dice al mozo al recibir de un proveedor (antes estaba en Ariadna).</summary>
        internal class TextosCompras : TextosTipoRecepcion
        {
            public override string Detalle(RecepcionPendienteDTO pendiente)
            {
                int pedidos = pendiente.Pedidos?.Count ?? 0;
                return Juntar($"Proveedor {pendiente.Documento}", pedidos > 0 ? Contar(pedidos, "pedido", "pedidos") : null,
                    $"{pendiente.Unidades} ud.", Fecha(pendiente.Fecha));
            }

            public override string AvisoCoincide => ENTRA_EN_LOS_PEDIDOS;
            public override string AvisoNoCoincide => ENTRA_EN_LOS_PEDIDOS;
            public override string AvisoConFaltas => "Lo que falta sigue pendiente o, si el proveedor no deja pendientes, se da por no servido.";
            public override string AvisoConRecuperadas => "Lo que se dio por no servido hace poco entra con su pedido.";
            public override string AvisoConSobras => "Lo que sobra entra de más, a la espera del visto bueno de Compras.";
            public override string AvisoConAjenos => "Lo que no se esperaba no entra: se avisa a Compras.";

            /// <summary>Solo va a Ubicar lo que entra en un albarán (con visto bueno).</summary>
            public override string AvisoUbicar(ResultadoTerminarRecepcionDTO resultado)
            {
                return (resultado.Documentos?.Count ?? 0) == 0
                    ? "Todavía no aparece en Ubicar: aparecerá cuando Compras le dé el visto bueno y se haga el albarán."
                    : "Lo que ha entrado en los albaranes ya aparece en Ubicar.";
            }

            private const string ENTRA_EN_LOS_PEDIDOS = "Lo leído entra en los pedidos del proveedor, del más antiguo al más reciente, y se hacen los albaranes.";
        }

        public bool PuedeTerminar(IPrincipal usuario, string empresa, string almacen)
        {
            return usuario != null && gruposQuePuedenTerminar.Any(g => usuario.IsInRoleSinDominio(g));
        }

        public async Task<List<RecepcionPendienteDTO>> LeerPendientes(string empresa, string almacen)
        {
            List<PedidoCompraPendienteDTO> pedidos = await repositorio.LeerPedidosPendientes(empresa, almacen).ConfigureAwait(false)
                ?? new List<PedidoCompraPendienteDTO>();
            return pedidos
                .Where(p => !string.IsNullOrWhiteSpace(p.Proveedor))
                .GroupBy(p => p.Proveedor.Trim())
                .Select(g => new RecepcionPendienteDTO
                {
                    Tipo = TIPO,
                    Documento = g.Key,
                    Titulo = g.Select(p => p.NombreProveedor?.Trim()).FirstOrDefault(n => !string.IsNullOrEmpty(n)) ?? $"Proveedor {g.Key}",
                    Fecha = g.Min(p => p.FechaRecepcion),
                    Pedidos = g.Select(p => p.Pedido).OrderBy(p => p).ToList(),
                    Lineas = g.Sum(p => p.Lineas),
                    Unidades = g.Sum(p => p.Unidades)
                })
                .ToList();
        }

        public async Task<List<RecepcionPendienteDTO>> BuscarPorCodigo(string empresa, string almacen, string codigo)
        {
            List<string> proveedores = await repositorio.ProveedoresConPendiente(empresa, almacen, codigo).ConfigureAwait(false) ?? new List<string>();
            if (!proveedores.Any())
            {
                return new List<RecepcionPendienteDTO>();
            }
            var buscados = new HashSet<string>(proveedores.Select(p => p?.Trim()), StringComparer.OrdinalIgnoreCase);
            return (await LeerPendientes(empresa, almacen).ConfigureAwait(false)).Where(r => buscados.Contains(r.Documento)).ToList();
        }

        public async Task<RecepcionDTO> LeerEsperado(string empresa, string almacen, string documento)
        {
            string proveedor = documento?.Trim();
            List<FilaRecepcionCompra> filas = await repositorio.LeerLineasPendientesProveedor(empresa, almacen, proveedor).ConfigureAwait(false)
                ?? new List<FilaRecepcionCompra>();
            if (!filas.Any())
            {
                return null;
            }

            var porProducto = filas
                .Where(f => !string.IsNullOrWhiteSpace(f.Producto))
                .GroupBy(f => f.Producto.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g => new
                {
                    Producto = g.Key,
                    Descripcion = g.Select(f => f.Descripcion?.Trim()).FirstOrDefault(d => !string.IsNullOrEmpty(d)),
                    Codigo = Productos.ValidadorGtin.SoloSiValido(g.Select(f => f.CodigoBarras?.Trim()).FirstOrDefault(c => !string.IsNullOrEmpty(c))), // NestoAPI#674
                    Cantidad = g.Where(f => f.Estado == PlanificadorRecepcionCompra.ESTADO_PENDIENTE).Sum(f => f.Cantidad),
                    // Lo dado por no servido hace poco: si llega, entra con su pedido (la más antigua primero)
                    Recuperables = g.Where(f => f.Estado == PlanificadorRecepcionCompra.ESTADO_ANULADA)
                        .OrderBy(f => f.FechaRecepcion).ThenBy(f => f.Pedido)
                        .Select(f => new RecuperableRecepcionDTO { Pedido = f.Pedido, Cantidad = f.Cantidad, FechaNoServido = (f.FechaRecepcion ?? DateTime.Today).Date })
                        .ToList()
                })
                .ToList();
            // Un código es duplicado si lo comparten productos distintos (no por estar el producto en dos pedidos)
            HashSet<string> duplicados = CasadorEscaneos.CodigosDuplicados(
                porProducto.Select(p => new KeyValuePair<string, string>(p.Producto, p.Codigo)));

            return new RecepcionDTO
            {
                Tipo = TIPO,
                Documento = proveedor,
                Titulo = filas.Select(f => f.NombreProveedor?.Trim()).FirstOrDefault(n => !string.IsNullOrEmpty(n)) ?? $"Proveedor {proveedor}",
                Empresa = empresa,
                Almacen = almacen,
                Lineas = porProducto.Select(p => new LineaRecepcionDTO
                {
                    Producto = p.Producto,
                    Descripcion = p.Descripcion,
                    CodigoBarras = p.Codigo,
                    SinCodigo = p.Codigo == null,
                    CodigoDuplicado = p.Codigo != null && duplicados.Contains(p.Codigo),
                    Cantidad = p.Cantidad,
                    Recuperables = p.Recuperables
                }).ToList()
            };
        }

        public async Task<ResultadoTerminarRecepcionDTO> Terminar(SolicitudTerminarRecepcion solicitud)
        {
            bool esCompras = solicitud.Principal != null && solicitud.Principal.IsInRoleSinDominio(Constantes.GruposSeguridad.COMPRAS);
            DateTime fecha = hoy().Date;
            PlanRecepcionCompra plan = null;

            ResultadoTerminarRecepcionDTO resultado = await repositorio.EnTransaccion(async transaccion =>
            {
                List<Guid> ids = solicitud.Lecturas.Keys.Select(p => IdEvidencia(solicitud.IdRecepcion, p)).ToList();
                if (await transaccion.YaRegistrada(solicitud.Empresa, ids).ConfigureAwait(false))
                {
                    return new ResultadoTerminarRecepcionDTO { Tipo = TIPO, Documento = solicitud.Documento, YaEstabaTerminada = true };
                }

                List<LineaCompraPendiente> lineas = await transaccion
                    .LeerLineasBloqueando(solicitud.Empresa, solicitud.Almacen, solicitud.Documento).ConfigureAwait(false)
                    ?? new List<LineaCompraPendiente>();
                if (!lineas.Any())
                {
                    throw new NestoBusinessException($"El proveedor {solicitud.Documento} no tiene nada pendiente de recibir en {solicitud.Almacen}.");
                }
                if (solicitud.Ensayo != null)
                {
                    await solicitud.Ensayo.Empezar(await transaccion.PrepararFoto(solicitud.Empresa,
                        lineas.Select(l => l.Pedido).Distinct().ToList()).ConfigureAwait(false)).ConfigureAwait(false);
                }

                plan = PlanificadorRecepcionCompra.Planificar(lineas, solicitud.Lecturas, fecha, esCompras);
                if (!plan.Recibidas.Any())
                {
                    throw new NestoBusinessException("Nada de lo leído está pedido a este proveedor (" +
                        string.Join(", ", plan.NoPedidos.Select(n => n.Producto)) + "): no se ha recibido nada.");
                }

                Dictionary<int, LineaCompraPendiente> porOrden = lineas.ToDictionary(l => l.NumeroOrden);
                foreach (LineaRecibida recibida in plan.Recibidas)
                {
                    if (recibida.Reactivada)
                    {
                        await transaccion.Reactivar(solicitud.Empresa, recibida.NumeroOrden, solicitud.Usuario).ConfigureAwait(false);
                    }
                    await transaccion.RecibirLinea(solicitud.Empresa, porOrden[recibida.NumeroOrden], recibida, fecha, solicitud.Usuario).ConfigureAwait(false);
                }
                foreach (int anulada in plan.Anuladas)
                {
                    await transaccion.Anular(solicitud.Empresa, anulada, solicitud.Usuario).ConfigureAwait(false);
                }
                foreach (ExcesoRecepcion exceso in plan.Excesos)
                {
                    await transaccion.CrearExceso(solicitud.Empresa, porOrden[exceso.CopiaDe], exceso, fecha, solicitud.Usuario).ConfigureAwait(false);
                }

                // prdCrearAlbaránCmp se lleva todo lo del pedido en estado 1, con visto bueno y fecha de hoy o antes:
                // lo que sigue pendiente pasa a mañana, como hace prdInsertarLineaCmp
                if (plan.Aplazadas.Any())
                {
                    await transaccion.Aplazar(solicitud.Empresa, plan.Aplazadas, plan.FechaAplazadas).ConfigureAwait(false);
                }
                var documentos = new List<DocumentoRecepcionDTO>();
                foreach (int pedido in plan.PedidosAAlbaranear)
                {
                    int albaran = await transaccion.CrearAlbaran(pedido, solicitud.Usuario).ConfigureAwait(false);
                    documentos.Add(new DocumentoRecepcionDTO { Pedido = pedido, Albaran = albaran });
                }

                await transaccion.RegistrarEvidencia(solicitud.Empresa, solicitud.Lecturas.Select(l => new EvidenciaRecepcion
                {
                    IdCliente = IdEvidencia(solicitud.IdRecepcion, l.Key),
                    IdRecepcion = solicitud.IdRecepcion,
                    NumeroOrigen = plan.PedidoDeProducto.TryGetValue(l.Key, out int pedidoDelProducto) ? pedidoDelProducto : 0,
                    Producto = l.Key,
                    Cantidad = l.Value,
                    Usuario = solicitud.Usuario,
                    Dispositivo = solicitud.Dispositivo
                }).ToList()).ConfigureAwait(false);
                // Ariadna#51: lo leído en curso con este IdRecepcion queda terminado con la recepción (lo que no llega sigue
                // pendiente en los pedidos: la próxima vez se abre otro IdRecepcion al leer)
                _ = await transaccion.TerminarLecturasEnCurso(ClaveLecturas(solicitud, TIPO), solicitud.IdRecepcion, false,
                    solicitud.Usuario, solicitud.Dispositivo).ConfigureAwait(false);
                if (solicitud.Ensayo != null)
                {
                    await solicitud.Ensayo.Acabar().ConfigureAwait(false);
                }

                return new ResultadoTerminarRecepcionDTO
                {
                    Tipo = TIPO,
                    Documento = solicitud.Documento,
                    Documentos = documentos,
                    NoEsperados = plan.NoPedidos
                        .Select(n => new DiferenciaPreparacionDTO { Producto = n.Producto, Leido = n.Cantidad, Ajeno = true })
                        .ToList(),
                    Avisos = plan.AvisosParaCompras
                        .Concat(!esCompras && plan.NoPedidos.Any() ? new[] { "Compras está avisado de lo que no estaba pedido." } : new string[0])
                        .ToList(),
                    Recuperadas = plan.Recuperadas.Select(r => new LineaRecuperadaDTO
                    {
                        Pedido = r.Pedido,
                        Producto = r.Producto,
                        Cantidad = r.Cantidad,
                        FechaNoServido = r.FechaNoServido,
                        Texto = r.Texto
                    }).ToList()
                };
            }, solicitud.Ensayo != null).ConfigureAwait(false);

            // Después de confirmar: un fallo al avisar no deshace la recepción. En un ensayo no se avisa a nadie
            List<string> paraCompras = plan == null ? new List<string>() : AvisosParaCompras(plan, solicitud, esCompras);
            if (paraCompras.Any() && !resultado.YaEstabaTerminada && solicitud.Ensayo == null)
            {
                try
                {
                    await avisador.Avisar($"Recepción del proveedor {solicitud.Documento} ({solicitud.Usuario})", paraCompras)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    ElmahHelper.Log(new Exception("[Recepción compras] No se pudo avisar a Compras de la recepción del proveedor " +
                        $"{solicitud.Documento}: {ex.Message}", ex));
                }
            }
            return resultado;
        }

        /// <summary>
        /// Lo que tiene que ver Compras: lo del plan (exceso y líneas sin visto bueno) y, decisión 3 de #559, lo que ha llegado
        /// sin estar pedido, que no entra (regalos y muestras, sin decidir aún cómo se reconocen: solo el aviso). Igual que
        /// el exceso, si lo recibe alguien de Compras no se avisa.
        /// </summary>
        private static List<string> AvisosParaCompras(PlanRecepcionCompra plan, SolicitudTerminarRecepcion solicitud, bool esCompras)
        {
            var avisos = plan.AvisosParaCompras.ToList();
            if (!esCompras)
            {
                avisos.AddRange(plan.NoPedidos.Select(n =>
                    $"Del producto {n.Producto} han llegado {n.Cantidad} ud. que no están pedidas al proveedor {solicitud.Documento}: " +
                    $"no han entrado (las ha recibido {solicitud.Usuario})."));
            }
            return avisos;
        }

        /// <summary>
        /// El IdCliente de la evidencia de un producto en una recepción: siempre el mismo para la misma recepción y
        /// producto, así un reenvío se reconoce y no se recibe dos veces.
        /// </summary>
        public static Guid IdEvidencia(Guid idRecepcion, string producto) => EvidenciasRecepcionSql.IdEvidencia(idRecepcion, producto);

        /// <summary>Ariadna#51: la recepción en curso de esta solicitud (la misma clave que usan GET/POST/DELETE de …/Lecturas).</summary>
        internal static ClaveRecepcionEnCurso ClaveLecturas(SolicitudTerminarRecepcion solicitud, string tipo)
        {
            return new ClaveRecepcionEnCurso
            {
                Empresa = solicitud.Empresa,
                Tipo = tipo,
                Documento = solicitud.Documento,
                Almacen = solicitud.Almacen
            };
        }
    }

    /// <summary>
    /// NestoAPI#553: recibir una reposición entre almacenes (el documento es el número de traspaso). Terminar es lo que
    /// hace hoy Nesto viejo al dar la entrada: contabilizar con prdExtrProducto el diario de entrada de reposiciones del
    /// almacén de destino (Almacenes.DiarioEntradaRep), con el usuario que la da. Ese procedimiento contabiliza el diario
    /// ENTERO: si hay otras reposiciones en él, entran también (como hoy) y se dice.
    ///
    /// <para>Diferencias (Carlos, 04/10/26): entra en el destino EXACTAMENTE lo leído (se ajusta el diario de entrada antes de
    /// contabilizar) y se informa a quien creó el traspaso (o al grupo Almacén) y en la respuesta (Diferencias, AvisadoA).</para>
    ///
    /// <para>Quién: hoy la entrada la da siempre alguien cuyo AlmacénPedidoVta es el almacén de destino (60 días: Reina →
    /// REI, Paloma → ALC, Andre/Alfredo/Santiago → ALG). Se exige lo mismo.</para>
    ///
    /// <para>Por partes (NestoAPI#673): con <see cref="SolicitudTerminarRecepcion.Parcial"/> entra solo lo leído y el resto del
    /// traspaso se queda en el diario, pendiente, con el mismo NºTraspaso (ver <see cref="ITransaccionCierreReposicion.ApartarNoLeido"/>).</para>
    /// </summary>
    public class OrigenRecepcionReposiciones : IOrigenRecepcion, IAdmiteRecepcionParcial
    {
        public const string TIPO = "REPO";
        private const string CLAVE_ALMACEN_USUARIO = "AlmacénPedidoVta";

        private readonly IServicioRecepcionReposiciones reposiciones;
        private readonly IRepositorioCierreReposiciones cierre;
        private readonly IAvisadorReposiciones avisador;
        private readonly Func<string, string, string> almacenDelUsuario;
        private readonly Reposiciones.IServicioDiferenciasReposicion diferenciasReposicion;
        private IRepositorioBultosReposicion bultos;

        /// <param name="diferenciasReposicion">NestoAPI#634: el ajuste del origen después de confirmar. Null: el de la BD, creado al usarlo.</param>
        /// <param name="bultos">NestoAPI#645: las etiquetas de bulto. Null: las de la BD, creado al usarlo.</param>
        public OrigenRecepcionReposiciones(IServicioRecepcionReposiciones reposiciones, IRepositorioCierreReposiciones cierre,
            IAvisadorReposiciones avisador, Reposiciones.IServicioDiferenciasReposicion diferenciasReposicion = null,
            IRepositorioBultosReposicion bultos = null)
            : this(reposiciones, cierre, avisador, null, diferenciasReposicion, bultos)
        {
        }

        /// <param name="almacenDelUsuario">(empresa, usuario sin dominio) → su AlmacénPedidoVta.</param>
        internal OrigenRecepcionReposiciones(IServicioRecepcionReposiciones reposiciones, IRepositorioCierreReposiciones cierre,
            IAvisadorReposiciones avisador, Func<string, string, string> almacenDelUsuario,
            Reposiciones.IServicioDiferenciasReposicion diferenciasReposicion = null, IRepositorioBultosReposicion bultos = null)
        {
            this.bultos = bultos;
            this.reposiciones = reposiciones;
            this.cierre = cierre;
            this.avisador = avisador;
            this.diferenciasReposicion = diferenciasReposicion;
            this.almacenDelUsuario = almacenDelUsuario
                ?? ((empresa, usuario) => Controllers.ParametrosUsuarioController.LeerParametro(empresa, usuario, CLAVE_ALMACEN_USUARIO));
        }

        public string Tipo => TIPO;
        public bool SeTerminaDesdeAqui => true;
        public TextosTipoRecepcion Textos { get; } = new TextosReposiciones();

        /// <summary>La hora de ahora (los tests la cambian): para avisar de las recibidas en parte hace más de 2 días.</summary>
        internal Func<DateTime> Ahora { get; set; } = () => DateTime.Now;

        /// <summary>
        /// NestoAPI#600: lo que se le dice al mozo al recibir una reposición (antes estaba en Ariadna). El detalle de la lista
        /// es el genérico («1 línea · 1 ud. · 06/10»): «Reposición» ya va en el título.
        /// </summary>
        internal class TextosReposiciones : TextosTipoRecepcion
        {
            public override string TituloConfirmacion(RecepcionDTO recepcion) => $"¿Terminar la reposición {recepcion.Documento} con esto?";

            // #553 (Carlos, 04/10): entra exactamente lo leído y, si no coincide, se avisa a quien hizo el traspaso
            public override string AvisoCoincide => "Entra la reposición entera en el almacén y queda pendiente de ubicar.";
            // #634: se avisa también a Almacén y queda constancia de la diferencia, pendiente de comprobar en el origen
            public override string AvisoNoCoincide =>
                "Lo leído no coincide con lo enviado: entra lo leído (no lo enviado), queda pendiente de ubicar y se avisa a quien hizo la reposición y a Almacén.";

            public override string AvisoUbicar(ResultadoTerminarRecepcionDTO resultado) => "Lo recibido ya aparece en Ubicar.";

            /// <summary>NestoAPI#673: «2 líneas · 3 ud. · 06/10 · Recibido en parte: 1 línea, 2 ud.» (lo de delante, lo que queda).</summary>
            public override string Detalle(RecepcionPendienteDTO pendiente)
                => Juntar(base.Detalle(pendiente), pendiente.TextoRecibidaEnParte);
        }

        /// <summary>
        /// «Reposición 80905 desde Alcobendas» (Carlos, 06/10/26): en Entradas se ve de dónde viene cada una. Sin origen
        /// conocido (no debería pasar: Delegación va siempre), «Reposición 80905».
        /// <para>Sugerencia 581: la puntual (la que una tienda manda aparte con solo unos productos), «Reposición puntual 80951
        /// desde Alcobendas»: así se distingue en Ariadna y en Nesto aunque no miren <see cref="RecepcionPendienteDTO.EsPuntual"/>.</para>
        /// </summary>
        internal static string Titulo(int traspaso, ReposicionPendienteDTO reposicion)
        {
            string origen = reposicion?.NombreOrigen?.Trim();
            if (string.IsNullOrWhiteSpace(origen))
            {
                origen = reposicion?.Origen?.Trim();
            }
            string reposicionN = reposicion?.EsPuntual == true ? $"Reposición puntual {traspaso}" : $"Reposición {traspaso}";
            return string.IsNullOrWhiteSpace(origen) ? reposicionN : $"{reposicionN} desde {origen}";
        }

        public bool PuedeTerminar(IPrincipal usuario, string empresa, string almacen)
        {
            string nombre = usuario?.Identity?.IsAuthenticated == true ? usuario.Identity.Name : null;
            if (string.IsNullOrWhiteSpace(nombre) || string.IsNullOrWhiteSpace(almacen))
            {
                return false;
            }
            string sinDominio = nombre.Contains("\\") ? nombre.Substring(nombre.LastIndexOf('\\') + 1) : nombre;
            string suyo = almacenDelUsuario(empresa, sinDominio.Trim());
            return !string.IsNullOrWhiteSpace(suyo) && string.Equals(suyo.Trim(), almacen.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        public async Task<List<RecepcionPendienteDTO>> LeerPendientes(string empresa, string almacen)
        {
            List<ReposicionPendienteDTO> pendientes = await reposiciones.LeerPendientes(empresa, almacen).ConfigureAwait(false)
                ?? new List<ReposicionPendienteDTO>();
            DateTime ahora = Ahora();
            return pendientes.Select(r =>
            {
                // NestoAPI#673: lo ya dado de alta en parciales; más de 2 días así, se avisa (decisión de Carlos, 19/10/26)
                string recibidaEnParte = TextoRecibidaEnParte(r.LineasRecibidas, r.UnidadesRecibidas);
                bool avisar = recibidaEnParte != null && r.FechaPrimeraParcial.HasValue && ahora - r.FechaPrimeraParcial.Value > PLAZO_RECIBIDA_EN_PARTE;
                return new RecepcionPendienteDTO
                {
                    Tipo = TIPO,
                    Documento = r.Traspaso.ToString(),
                    Titulo = Titulo(r.Traspaso, r),
                    EsPuntual = r.EsPuntual,
                    Fecha = r.Fecha,
                    Lineas = r.Lineas,
                    Unidades = r.Unidades,
                    RecibidaEnParte = recibidaEnParte != null,
                    LineasRecibidas = r.LineasRecibidas,
                    UnidadesRecibidas = r.UnidadesRecibidas,
                    FechaPrimeraParcial = recibidaEnParte != null ? r.FechaPrimeraParcial : null,
                    TextoRecibidaEnParte = recibidaEnParte,
                    AvisarRecibidaEnParte = avisar,
                    AvisoRecibidaEnParte = avisar
                        ? $"La reposición {r.Traspaso} lleva más de 2 días recibida solo en parte: termine de recibirla."
                        : null
                };
            }).ToList();
        }

        private IRepositorioBultosReposicion Bultos => bultos ?? (bultos = RepositorioBultosReposicionSql.ConContextoPropio());

        public async Task<List<RecepcionPendienteDTO>> BuscarPorCodigo(string empresa, string almacen, string codigo)
        {
            var encontradas = new List<RecepcionPendienteDTO>();
            List<RecepcionPendienteDTO> pendientes = await LeerPendientes(empresa, almacen).ConfigureAwait(false);
            // NestoAPI#645: la etiqueta del bulto («R81010-2») lleva a su reposición, si está pendiente aquí
            if (CodigosBultoReposicion.Leer(codigo, out int traspasoEtiqueta, out _))
            {
                return pendientes.Where(p => p.Documento == traspasoEtiqueta.ToString(CultureInfo.InvariantCulture)).ToList();
            }
            foreach (RecepcionPendienteDTO pendiente in pendientes)
            {
                RecepcionDTO esperado = await LeerSinBultos(empresa, almacen, pendiente.Documento).ConfigureAwait(false);
                if (esperado != null && esperado.Lineas.Any(l =>
                    string.Equals(l.Producto?.Trim(), codigo, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(l.CodigoBarras?.Trim(), codigo, StringComparison.OrdinalIgnoreCase)))
                {
                    encontradas.Add(pendiente);
                }
            }
            return encontradas;
        }

        public async Task<RecepcionDTO> LeerEsperado(string empresa, string almacen, string documento)
        {
            RecepcionDTO recepcion = await LeerSinBultos(empresa, almacen, documento).ConfigureAwait(false);
            if (recepcion != null)
            {
                // NestoAPI#645: la transición es automática: las cerradas sin etiquetas (las de antes) se reciben como siempre
                List<FilaBultoReposicion> filas = await Bultos.Leer(empresa, int.Parse(recepcion.Documento, CultureInfo.InvariantCulture)).ConfigureAwait(false);
                recepcion.Bultos = filas?.Count ?? 0;
                recepcion.ExigeEtiqueta = recepcion.Bultos > 0;
            }
            return recepcion;
        }

        private async Task<RecepcionDTO> LeerSinBultos(string empresa, string almacen, string documento)
        {
            if (!int.TryParse(documento?.Trim(), out int traspaso))
            {
                throw new NestoBusinessException($"«{documento}» no es un número de traspaso.");
            }
            RecepcionReposicionDTO recepcion = await reposiciones.LeerRecepcion(empresa, almacen, traspaso).ConfigureAwait(false);
            if (recepcion == null)
            {
                return null;
            }
            // El origen sale de la lista de pendientes (PreExtrProducto es pequeña: dos filas hoy); el mismo título que en la lista
            ReposicionPendienteDTO pendiente = (await reposiciones.LeerPendientes(empresa, almacen).ConfigureAwait(false) ?? new List<ReposicionPendienteDTO>())
                .FirstOrDefault(r => r.Traspaso == traspaso);
            return new RecepcionDTO
            {
                Tipo = TIPO,
                Documento = traspaso.ToString(),
                Titulo = Titulo(traspaso, pendiente),
                EsPuntual = pendiente?.EsPuntual == true,
                TextoRecibidaEnParte = pendiente == null ? null : TextoRecibidaEnParte(pendiente.LineasRecibidas, pendiente.UnidadesRecibidas),
                // Ariadna#51: lo ya dado de alta en parte sigue en la lista («Recibido el 22/10 (Alfredo)»); solo si lo hay
                YaRecibidas = pendiente != null && TextoRecibidaEnParte(pendiente.LineasRecibidas, pendiente.UnidadesRecibidas) != null
                    ? await reposiciones.LeerYaRecibidas(recepcion.Empresa ?? empresa, traspaso).ConfigureAwait(false)
                    : null,
                Empresa = recepcion.Empresa,
                Almacen = recepcion.Almacen,
                Lineas = recepcion.Lineas.Select(l => new LineaRecepcionDTO
                {
                    Producto = l.Producto,
                    Descripcion = l.Descripcion,
                    CodigoBarras = l.CodigoBarras,
                    SinCodigo = l.SinCodigo,
                    CodigoDuplicado = l.CodigoDuplicado,
                    Cantidad = l.Cantidad
                }).ToList()
            };
        }

        public async Task<ResultadoTerminarRecepcionDTO> Terminar(SolicitudTerminarRecepcion solicitud)
        {
            if (!int.TryParse(solicitud.Documento?.Trim(), out int traspaso))
            {
                throw new NestoBusinessException($"«{solicitud.Documento}» no es un número de traspaso.");
            }
            List<DiferenciaPreparacionDTO> diferencias = new List<DiferenciaPreparacionDTO>();
            DatosTraspasoReposicion datos = null;
            List<int> registradas = new List<int>();
            // NestoAPI#673: se da de alta solo lo leído y el resto sigue pendiente (lo enviado frente a lo leído, para contarlo)
            bool parcial = false;
            List<DiferenciaPreparacionDTO> pendiente = null;

            ResultadoTerminarRecepcionDTO resultado = await cierre.EnTransaccion(async transaccion =>
            {
                List<Guid> ids = solicitud.Lecturas.Keys.Select(p => EvidenciasRecepcionSql.IdEvidencia(solicitud.IdRecepcion, p)).ToList();
                if (await transaccion.YaRegistrada(solicitud.Empresa, ids).ConfigureAwait(false))
                {
                    return new ResultadoTerminarRecepcionDTO { Tipo = TIPO, Documento = solicitud.Documento, YaEstabaTerminada = true };
                }
                // NestoAPI#645: cada bulto en su reposición (antes de tocar nada)
                List<string> bultosLeidos = await ComprobarEtiquetas(transaccion, solicitud, traspaso).ConfigureAwait(false);

                string diario = await transaccion.DiarioDeEntrada(solicitud.Empresa, solicitud.Almacen).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(diario))
                {
                    throw new NestoBusinessException($"El almacén {solicitud.Almacen} no tiene diario de entrada de reposiciones.");
                }
                List<int> enDiario = await transaccion.TraspasosEnDiario(solicitud.Empresa, solicitud.Almacen, diario).ConfigureAwait(false)
                    ?? new List<int>();
                if (!enDiario.Contains(traspaso))
                {
                    // NestoAPI#577: rellenada en el origen pero sin salir todavía (sin contabilizar la salida): 409
                    await reposiciones.ComprobarQueHaSalido(solicitud.Empresa, solicitud.Almacen, traspaso).ConfigureAwait(false);
                    throw new NestoBusinessException($"La reposición {traspaso} ya no está pendiente de entrar en {solicitud.Almacen}.");
                }
                // NestoAPI#553: el diario de entrada se contabiliza entero; las demás reposiciones (y cualquier otra línea) se
                // apartan para que entre solo la que se ha leído, y se devuelven después (como se hacía a mano con «RepoEscond»)
                List<int> apartadas = await transaccion.ApartarOtros(solicitud.Empresa, diario, traspaso).ConfigureAwait(false)
                    ?? new List<int>();
                if (solicitud.Ensayo != null)
                {
                    await solicitud.Ensayo.Empezar(await transaccion.PrepararFoto(solicitud.Empresa, diario, new List<int> { traspaso })
                        .ConfigureAwait(false)).ConfigureAwait(false);
                }
                if (await transaccion.FilasQueSoloSabeNestoViejo(solicitud.Empresa, diario).ConfigureAwait(false) > 0)
                {
                    throw new NestoBusinessException($"El diario de entrada {diario} tiene líneas con hueco o en negativo, que desde aquí no se " +
                        $"tratarían como en Nesto viejo. No se ha recibido nada: {HAZLA_EN_NESTO_VIEJO}") { StatusCode = HttpStatusCode.Conflict };
                }

                // #553 (Carlos, 04/10/26): «si damos de alta el producto debe ser de lo leído». Lo que entra en el destino es
                // EXACTAMENTE lo leído: se ajusta el diario de entrada de este traspaso antes de contabilizar (lo que no venía,
                // primero: así siempre queda una fila del traspaso que copiar). La salida del origen, ya contabilizada, no se
                // toca aquí: NestoAPI#634 la corrige después de confirmar, con un apunte aparte en _ErrRepo (si el interruptor
                // Reposiciones:AjustarStockDiferencias está encendido)
                List<FilaReposicion> enviado = await transaccion.LeerLineas(solicitud.Empresa, solicitud.Almacen, traspaso).ConfigureAwait(false)
                    ?? new List<FilaReposicion>();
                List<DiferenciaPreparacionDTO> comparado = Diferencias(enviado, solicitud.Lecturas);
                // Novedad 751 (81028): lo ya dado de alta en una recepción en parte ya no está en el diario. Si se vuelve a leer
                // sale como «no se esperaba» y, al terminar, entraría OTRA VEZ en el stock (lo leído que no venía, entra)
                await ComprobarQueNoSeRepiteLoRecibidoEnParte(transaccion, solicitud, traspaso, comparado).ConfigureAwait(false);
                if (solicitud.Parcial)
                {
                    ComprobarParcial(comparado, traspaso);
                    // Si lo leído es exactamente lo enviado, la parcial lo completa todo: es la recepción entera (la de siempre)
                    parcial = comparado.Any();
                }
                var noLeidas = new List<int>();
                if (parcial)
                {
                    // NestoAPI#673: entra SOLO lo leído. Lo no leído del propio traspaso se aparta como las otras reposiciones
                    // (partiendo la fila que tenga parte leída y parte no), se contabiliza y vuelve al diario con el mismo
                    // NºTraspaso: la reposición sigue pendiente con lo que queda. Ni diferencias ni faltas: se comparan en la
                    // recepción que la termine, sobre lo que quede
                    pendiente = comparado;
                    noLeidas = await transaccion.ApartarNoLeido(solicitud.Empresa, solicitud.Almacen, diario, traspaso,
                        new Dictionary<string, int>(solicitud.Lecturas, StringComparer.OrdinalIgnoreCase)).ConfigureAwait(false) ?? new List<int>();
                    if (solicitud.Ensayo != null)
                    {
                        await solicitud.Ensayo.FotografiarTrasAjustar().ConfigureAwait(false);
                    }
                }
                else
                {
                    diferencias = comparado;
                }
                if (diferencias.Any())
                {
                    datos = await transaccion.LeerDatosTraspaso(solicitud.Empresa, solicitud.Almacen, diario, traspaso).ConfigureAwait(false)
                        ?? new DatosTraspasoReposicion();
                    foreach (DiferenciaPreparacionDTO diferencia in diferencias.OrderByDescending(d => d.Ajeno))
                    {
                        await transaccion.AjustarALoLeido(solicitud.Empresa, solicitud.Almacen, diario, traspaso, diferencia.Producto, diferencia.Leido)
                            .ConfigureAwait(false);
                    }
                    if (solicitud.Ensayo != null)
                    {
                        await solicitud.Ensayo.FotografiarTrasAjustar().ConfigureAwait(false);
                    }
                }

                // prdExtrProducto, llamado desde la API, no deja nada pendiente de ubicar (busca el almacén de SYSTEM_USER):
                // se calcula antes igual que él (ya con lo leído) y se pone después, en la misma transacción
                // Novedad 751 (81028): sin nada leído (lo que quedaba tras recibir en parte no ha llegado) no entra nada. El
                // ajuste ya ha quitado sus líneas del diario: no hay nada que contabilizar ni que ubicar
                bool entraAlgo = solicitud.Lecturas.Values.Any(c => c > 0);
                PendientesDeUbicarEntrada pendientesDeUbicar = entraAlgo
                    ? await transaccion.LeerPendientesDeUbicar(solicitud.Empresa, diario).ConfigureAwait(false)
                    : null;
                if (entraAlgo)
                {
                    await transaccion.Contabilizar(solicitud.Empresa, diario, solicitud.Usuario).ConfigureAwait(false);
                }
                await transaccion.DevolverApartadas(solicitud.Empresa, diario, apartadas.Concat(noLeidas).ToList()).ConfigureAwait(false);
                if (entraAlgo)
                {
                    _ = await transaccion.DejarPendientesDeUbicar(solicitud.Empresa, pendientesDeUbicar, solicitud.Usuario).ConfigureAwait(false);
                }
                // NestoAPI#673: en una parcial la reposición sigue abierta: ni cabecera recibida ni bultos leídos (eso, en la final)
                if (!parcial)
                {
                    // NestoAPI#577: quién y cuándo la ha recibido, en su cabecera (si la creó la API)
                    await transaccion.MarcarReposicionRecibida(solicitud.Empresa, traspaso, solicitud.Usuario).ConfigureAwait(false);
                    // NestoAPI#645: los bultos leídos (en un ensayo no se marca nada)
                    if (solicitud.Ensayo == null && bultosLeidos.Any())
                    {
                        await transaccion.MarcarBultosLeidos(solicitud.Empresa, bultosLeidos, solicitud.Usuario).ConfigureAwait(false);
                    }
                }
                // Quién y cuándo ha recibido cada cosa; NestoAPI#673: la de una parcial, marcada (es lo que dice «Recibido en parte»)
                await transaccion.RegistrarEvidencia(solicitud.Empresa, solicitud.Lecturas.Select(l => new EvidenciaRecepcion
                {
                    IdCliente = EvidenciasRecepcionSql.IdEvidencia(solicitud.IdRecepcion, l.Key),
                    IdRecepcion = solicitud.IdRecepcion,
                    NumeroOrigen = traspaso,
                    Producto = l.Key,
                    Cantidad = l.Value,
                    Usuario = solicitud.Usuario,
                    Dispositivo = solicitud.Dispositivo,
                    Parcial = parcial
                }).ToList()).ConfigureAwait(false);
                // Ariadna#51: lo leído en curso con este IdRecepcion queda terminado con la recepción; si ha sido en parte, se abre
                // otro IdRecepcion para lo que queda
                _ = await transaccion.TerminarLecturasEnCurso(OrigenRecepcionCompras.ClaveLecturas(solicitud, TIPO), solicitud.IdRecepcion, parcial,
                    solicitud.Usuario, solicitud.Dispositivo).ConfigureAwait(false);
                // NestoAPI#634: constancia de cada diferencia, con la entrada (el ajuste del origen va después de confirmar)
                if (diferencias.Any())
                {
                    registradas = await transaccion.RegistrarDiferencias(diferencias.Select(d => new Reposiciones.DiferenciaReposicionNueva
                    {
                        Empresa = solicitud.Empresa,
                        Traspaso = traspaso,
                        Origen = datos?.Origen?.Trim(),
                        Destino = solicitud.Almacen,
                        Producto = d.Producto,
                        Enviado = d.Esperado,
                        Leido = d.Leido,
                        UsuarioRecepcion = solicitud.Usuario,
                        Preparador = datos?.Creador?.Trim(),
                        IdRecepcion = solicitud.IdRecepcion
                    }).ToList()).ConfigureAwait(false) ?? new List<int>();
                }

                var avisos = new List<string>();
                int lineasPendientes = 0, unidadesPendientes = 0;
                if (parcial)
                {
                    List<DiferenciaPreparacionDTO> quedan = pendiente.Where(d => d.Leido < d.Esperado).ToList();
                    lineasPendientes = quedan.Count;
                    unidadesPendientes = quedan.Sum(d => d.Esperado - d.Leido);
                    avisos.Add(AvisoParcial(solicitud.Lecturas.Count, solicitud.Lecturas.Values.Sum(), lineasPendientes, unidadesPendientes));
                }
                if (solicitud.Ensayo != null)
                {
                    await solicitud.Ensayo.Acabar().ConfigureAwait(false);
                }
                return new ResultadoTerminarRecepcionDTO
                {
                    Tipo = TIPO,
                    Documento = traspaso.ToString(),
                    Avisos = avisos,
                    Diferencias = diferencias,
                    Parcial = parcial,
                    LineasPendientes = lineasPendientes,
                    UnidadesPendientes = unidadesPendientes
                };
            }, solicitud.Ensayo != null).ConfigureAwait(false);

            if (resultado.YaEstabaTerminada || !diferencias.Any())
            {
                return resultado;
            }

            string destinatario = string.IsNullOrWhiteSpace(datos?.Creador) ? null : datos.Creador.Trim();
            string nombre = destinatario == null ? "el grupo " + Constantes.GruposSeguridad.ALMACEN
                : destinatario.Substring(destinatario.LastIndexOf('\\') + 1);
            // #634: se avisa a quien la preparó Y al grupo Almacén
            string avisados = destinatario == null ? nombre : $"{nombre} y el grupo {Constantes.GruposSeguridad.ALMACEN}";
            resultado.Avisos = resultado.Avisos ?? new List<string>();
            if (solicitud.Ensayo != null)
            {
                resultado.Avisos.Add($"Lo leído no coincide con lo enviado: entraría lo leído y se avisaría a {avisados} (en un ensayo no se avisa a nadie).");
                return resultado;
            }
            resultado.Avisos.Add("Lo leído no coincide con lo enviado: ha entrado lo leído y " +
                (destinatario == null ? $"{nombre} está avisado" : $"{avisados} están avisados") + " de las diferencias.");
            resultado.AvisadoA = destinatario ?? Constantes.GruposSeguridad.ALMACEN;
            string origen = string.IsNullOrWhiteSpace(datos?.Origen) ? "¿?" : datos.Origen.Trim();

            // NestoAPI#634: después de confirmar, el ajuste del stock del origen (cada diferencia en su transacción: nunca deshace la entrada)
            List<Reposiciones.ResultadoAjusteDiferencia> ajustes = await AjustarOrigen(solicitud, registradas, traspaso).ConfigureAwait(false);
            if (registradas.Any())
            {
                resultado.Avisos.Add($"La diferencia queda pendiente de comprobar en {origen} (Diferencias de reposición).");
            }
            if (ajustes.Any(a => a.Ajuste == Reposiciones.AjustesDiferenciaReposicion.HECHO))
            {
                resultado.Avisos.Add($"Se ha corregido el stock de {origen} con la diferencia.");
            }

            // Después de confirmar: un fallo al avisar no deshace la entrada
            try
            {
                await avisador.Avisar(destinatario, $"Reposición {traspaso} ({origen} → {solicitud.Almacen}): lo recibido no coincide con lo enviado",
                    LineasAviso(diferencias, origen, solicitud, ajustes, registradas.Any()), solicitud.Empresa, datos?.Origen).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Recepción reposiciones] No se pudo avisar de las diferencias de la reposición {traspaso}: {ex.Message}", ex));
            }
            return resultado;
        }

        internal const string HAZLA_EN_NESTO_VIEJO = "haz esta entrada en Nesto viejo o avisa a Andre.";

        /// <summary>
        /// NestoAPI#673: en una parcial solo entra lo que venía y como mucho lo enviado de cada producto. Lo que no venía o lo
        /// que sobra se ve al terminarla (que es la que compara y registra las diferencias de lo que quede).
        /// </summary>
        internal static void ComprobarParcial(IEnumerable<DiferenciaPreparacionDTO> comparado, int traspaso)
        {
            List<DiferenciaPreparacionDTO> ajenos = comparado.Where(d => d.Ajeno && d.Leido > 0).ToList();
            if (ajenos.Any())
            {
                throw new NestoBusinessException($"Para dar de alta solo lo leído, todo tiene que venir en la reposición {traspaso}: " +
                    string.Join(", ", ajenos.Select(d => d.Producto)) + " no venía. Quítelo de lo leído o termine la reposición entera. " +
                    "No se ha recibido nada.");
            }
            List<DiferenciaPreparacionDTO> sobran = comparado.Where(d => !d.Ajeno && d.Leido > d.Esperado).ToList();
            if (sobran.Any())
            {
                throw new NestoBusinessException("Para dar de alta solo lo leído, no puede haber más de lo enviado: " +
                    string.Join(", ", sobran.Select(d => $"{d.Producto} (enviado {d.Esperado}, leído {d.Leido})")) +
                    ". Corrija lo leído o termine la reposición entera. No se ha recibido nada.");
            }
        }

        internal static string AvisoParcial(int lineasRecibidas, int unidadesRecibidas, int lineasPendientes, int unidadesPendientes)
        {
            return $"Se ha dado de alta lo leído ({TextosTipoRecepcion.Contar(lineasRecibidas, "línea", "líneas")}, {unidadesRecibidas} ud.). " +
                $"Sigue pendiente de recibir el resto de la reposición ({TextosTipoRecepcion.Contar(lineasPendientes, "línea", "líneas")}, " +
                $"{unidadesPendientes} ud.): léalo y termine la reposición después; las diferencias se comprobarán entonces.";
        }

        /// <summary>«Recibido en parte: 1 línea, 2 ud.». Null si no se ha recibido nada en parte.</summary>
        internal static string TextoRecibidaEnParte(int lineas, int unidades)
        {
            return lineas <= 0 && unidades <= 0 ? null
                : $"Recibido en parte: {TextosTipoRecepcion.Contar(lineas, "línea", "líneas")}, {unidades} ud.";
        }

        /// <summary>Decisión de Carlos (19/10/26): más de 2 días recibida en parte y sin terminar, se avisa.</summary>
        internal static readonly TimeSpan PLAZO_RECIBIDA_EN_PARTE = TimeSpan.FromDays(2);

        /// <summary>
        /// Novedad 751: rechaza (sin tocar nada) lo leído que no venía cuando es un producto de esta reposición ya dado de alta en
        /// una recepción en parte. Lo ajeno de verdad (un producto que no era de la reposición) sigue entrando como siempre.
        /// </summary>
        private static async Task ComprobarQueNoSeRepiteLoRecibidoEnParte(ITransaccionCierreReposicion transaccion,
            SolicitudTerminarRecepcion solicitud, int traspaso, IEnumerable<DiferenciaPreparacionDTO> comparado)
        {
            List<DiferenciaPreparacionDTO> ajenos = comparado.Where(d => d.Ajeno && d.Leido > 0).ToList();
            if (!ajenos.Any())
            {
                return;
            }
            var recibidos = new HashSet<string>((await transaccion.ProductosRecibidosEnParte(solicitud.Empresa, traspaso).ConfigureAwait(false)
                ?? new List<string>()).Where(p => p != null).Select(p => p.Trim()), StringComparer.OrdinalIgnoreCase);
            List<string> repetidos = ajenos.Select(d => d.Producto?.Trim()).Where(recibidos.Contains).ToList();
            if (repetidos.Any())
            {
                throw new NestoBusinessException($"{string.Join(", ", repetidos)} ya se {(repetidos.Count == 1 ? "dio" : "dieron")} de alta " +
                    $"en esta reposición {traspaso} (recibida en parte): no se puede recibir otra vez. {(repetidos.Count == 1 ? "Quítelo" : "Quítelos")} de lo leído (en Ariadna, " +
                    "«Descartar lo leído») y termine con lo que quede. No se ha recibido nada.");
            }
        }

        /// <summary>
        /// NestoAPI#645: si el traspaso tiene etiquetas de bulto, hace falta al menos una suya entre las leídas; una etiqueta de
        /// otra reposición no se admite nunca (ese bulto es de la otra). Las que no son de ninguna se ignoran. Un bulto ya leído
        /// en otra recepción (parcial) no impide nada. Devuelve los códigos de este traspaso, para marcarlos. Sin la tabla, nada.
        /// </summary>
        private static async Task<List<string>> ComprobarEtiquetas(ITransaccionCierreReposicion transaccion, SolicitudTerminarRecepcion solicitud, int traspaso)
        {
            List<FilaBultoReposicion> delTraspaso = await transaccion.LeerBultos(solicitud.Empresa, traspaso).ConfigureAwait(false);
            if (delTraspaso == null)
            {
                return new List<string>();
            }
            // NestoAPI#675: todo en la forma normalizada («R81029'1» es «R81029-1»), lo guardado y lo leído
            var suyos = new HashSet<string>(delTraspaso.Select(b => CodigosBultoReposicion.Normalizar(b.Codigo)).Where(c => c != null));
            var leidos = new List<string>();
            foreach (string codigo in (solicitud.CodigosBulto ?? new List<string>())
                .Select(CodigosBultoReposicion.Normalizar).Where(c => c != null).Distinct())
            {
                if (suyos.Contains(codigo))
                {
                    leidos.Add(codigo);
                    continue;
                }
                FilaBultoReposicion otro = await transaccion.LeerBulto(solicitud.Empresa, codigo).ConfigureAwait(false);
                if (otro != null && otro.Traspaso != traspaso)
                {
                    throw new NestoBusinessException($"Esa etiqueta es de la reposición {otro.Traspaso}, no de la {traspaso}.");
                }
            }
            if (suyos.Any() && !leidos.Any())
            {
                throw new NestoBusinessException($"Lea la etiqueta del bulto antes de recibir la reposición {traspaso}.");
            }
            return leidos;
        }

        /// <summary>
        /// Solo los productos en que lo leído no es lo enviado (de menos, de más, o que no venían), con lo enviado en Esperado.
        /// Vacía si coincide exactamente.
        /// </summary>
        internal static List<DiferenciaPreparacionDTO> Diferencias(IEnumerable<FilaReposicion> enviado, IDictionary<string, int> leido)
        {
            return CasadorEscaneos.Casar(
                (enviado ?? Enumerable.Empty<FilaReposicion>()).Select(f => new CasadorEscaneos.Cantidad { Producto = f.Producto, Descripcion = f.Descripcion, Unidades = f.Cantidad }),
                (leido ?? new Dictionary<string, int>()).Select(l => new CasadorEscaneos.Cantidad { Producto = l.Key, Unidades = l.Value }))
                .Where(d => d.Ajeno || d.Diferencia != 0)
                .ToList();
        }

        /// <summary>NestoAPI#634: el ajuste del origen de las diferencias registradas. Nunca lanza.</summary>
        private async Task<List<Reposiciones.ResultadoAjusteDiferencia>> AjustarOrigen(SolicitudTerminarRecepcion solicitud, List<int> registradas, int traspaso)
        {
            if (registradas == null || !registradas.Any())
            {
                return new List<Reposiciones.ResultadoAjusteDiferencia>();
            }
            Reposiciones.ServicioDiferenciasReposicion propio = null;
            try
            {
                Reposiciones.IServicioDiferenciasReposicion servicio = diferenciasReposicion ?? (propio = new Reposiciones.ServicioDiferenciasReposicion());
                return await servicio.AjustarOrigenTrasRecibir(solicitud.Empresa, registradas, solicitud.Usuario).ConfigureAwait(false)
                    ?? new List<Reposiciones.ResultadoAjusteDiferencia>();
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Recepción reposiciones] No se pudo ajustar el origen de las diferencias de la reposición {traspaso}: {ex.Message}", ex));
                return new List<Reposiciones.ResultadoAjusteDiferencia>();
            }
            finally
            {
                propio?.Dispose();
            }
        }

        internal static List<string> LineasAviso(IEnumerable<DiferenciaPreparacionDTO> diferencias, string origen, SolicitudTerminarRecepcion solicitud,
            IReadOnlyCollection<Reposiciones.ResultadoAjusteDiferencia> ajustes = null, bool registradas = false)
        {
            List<string> lineas = diferencias.Select(d =>
                $"{d.Producto}: {(d.Ajeno ? "no venía" : $"enviado {d.Esperado}")}, leído {d.Leido}, diferencia {(d.Diferencia > 0 ? "+" : "")}{d.Diferencia}" +
                (string.IsNullOrWhiteSpace(d.Descripcion) ? "" : $" ({d.Descripcion})")).ToList();
            List<Reposiciones.ResultadoAjusteDiferencia> todos = (ajustes ?? new Reposiciones.ResultadoAjusteDiferencia[0]).ToList();
            List<Reposiciones.ResultadoAjusteDiferencia> hechos = todos.Where(a => a.Ajuste == Reposiciones.AjustesDiferenciaReposicion.HECHO).ToList();
            // El ajuste devuelve la diferencia al origen: lo que faltó entra (+), lo que sobró sale (−)
            string stockOrigen = hechos.Any()
                ? $"El stock de {origen} se ha corregido: " +
                  string.Join(", ", hechos.Select(a => $"{a.Producto} {(a.Diferencia < 0 ? "+" : "-")}{Math.Abs(a.Diferencia)}")) + "."
                : $"La salida de {origen} no se ha tocado.";
            lineas.Add($"Ha entrado en {solicitud.Almacen} lo leído (lo ha recibido {solicitud.Usuario}). {stockOrigen}");
            lineas.AddRange(todos.Where(a => !string.IsNullOrWhiteSpace(a.Mensaje)).Select(a => $"{a.Producto}: {a.Mensaje}"));
            if (registradas)
            {
                lineas.Add($"Pendiente de comprobar en {origen}: vea «Diferencias de reposición».");
            }
            return lineas;
        }
    }
}
