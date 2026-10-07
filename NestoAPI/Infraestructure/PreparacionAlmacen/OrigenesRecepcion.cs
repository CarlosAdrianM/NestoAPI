using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infrastructure;
using NestoAPI.Models;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
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
                    Codigo = g.Select(f => f.CodigoBarras?.Trim()).FirstOrDefault(c => !string.IsNullOrEmpty(c)),
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
    /// </summary>
    public class OrigenRecepcionReposiciones : IOrigenRecepcion
    {
        public const string TIPO = "REPO";
        private const string CLAVE_ALMACEN_USUARIO = "AlmacénPedidoVta";

        private readonly IServicioRecepcionReposiciones reposiciones;
        private readonly IRepositorioCierreReposiciones cierre;
        private readonly IAvisadorReposiciones avisador;
        private readonly Func<string, string, string> almacenDelUsuario;

        public OrigenRecepcionReposiciones(IServicioRecepcionReposiciones reposiciones, IRepositorioCierreReposiciones cierre,
            IAvisadorReposiciones avisador)
            : this(reposiciones, cierre, avisador, null)
        {
        }

        /// <param name="almacenDelUsuario">(empresa, usuario sin dominio) → su AlmacénPedidoVta.</param>
        internal OrigenRecepcionReposiciones(IServicioRecepcionReposiciones reposiciones, IRepositorioCierreReposiciones cierre,
            IAvisadorReposiciones avisador, Func<string, string, string> almacenDelUsuario)
        {
            this.reposiciones = reposiciones;
            this.cierre = cierre;
            this.avisador = avisador;
            this.almacenDelUsuario = almacenDelUsuario
                ?? ((empresa, usuario) => Controllers.ParametrosUsuarioController.LeerParametro(empresa, usuario, CLAVE_ALMACEN_USUARIO));
        }

        public string Tipo => TIPO;
        public bool SeTerminaDesdeAqui => true;
        public TextosTipoRecepcion Textos { get; } = new TextosReposiciones();

        /// <summary>
        /// NestoAPI#600: lo que se le dice al mozo al recibir una reposición (antes estaba en Ariadna). El detalle de la lista
        /// es el genérico («1 línea · 1 ud. · 06/10»): «Reposición» ya va en el título.
        /// </summary>
        internal class TextosReposiciones : TextosTipoRecepcion
        {
            public override string TituloConfirmacion(RecepcionDTO recepcion) => $"¿Terminar la reposición {recepcion.Documento} con esto?";

            // #553 (Carlos, 04/10): entra exactamente lo leído y, si no coincide, se avisa a quien hizo el traspaso
            public override string AvisoCoincide => "Entra la reposición entera en el almacén y queda pendiente de ubicar.";
            public override string AvisoNoCoincide =>
                "Lo leído no coincide con lo enviado: entra lo leído (no lo enviado), queda pendiente de ubicar y se avisa a quien hizo la reposición.";

            public override string AvisoUbicar(ResultadoTerminarRecepcionDTO resultado) => "Lo recibido ya aparece en Ubicar.";
        }

        /// <summary>
        /// «Reposición 80905 desde Alcobendas» (Carlos, 06/10/26): en Entradas se ve de dónde viene cada una. Sin origen
        /// conocido (no debería pasar: Delegación va siempre), «Reposición 80905».
        /// </summary>
        internal static string Titulo(int traspaso, ReposicionPendienteDTO reposicion)
        {
            string origen = reposicion?.NombreOrigen?.Trim();
            if (string.IsNullOrWhiteSpace(origen))
            {
                origen = reposicion?.Origen?.Trim();
            }
            return string.IsNullOrWhiteSpace(origen) ? $"Reposición {traspaso}" : $"Reposición {traspaso} desde {origen}";
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
            return pendientes.Select(r => new RecepcionPendienteDTO
            {
                Tipo = TIPO,
                Documento = r.Traspaso.ToString(),
                Titulo = Titulo(r.Traspaso, r),
                Fecha = r.Fecha,
                Lineas = r.Lineas,
                Unidades = r.Unidades
            }).ToList();
        }

        public async Task<List<RecepcionPendienteDTO>> BuscarPorCodigo(string empresa, string almacen, string codigo)
        {
            var encontradas = new List<RecepcionPendienteDTO>();
            foreach (RecepcionPendienteDTO pendiente in await LeerPendientes(empresa, almacen).ConfigureAwait(false))
            {
                RecepcionDTO esperado = await LeerEsperado(empresa, almacen, pendiente.Documento).ConfigureAwait(false);
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

            ResultadoTerminarRecepcionDTO resultado = await cierre.EnTransaccion(async transaccion =>
            {
                List<Guid> ids = solicitud.Lecturas.Keys.Select(p => EvidenciasRecepcionSql.IdEvidencia(solicitud.IdRecepcion, p)).ToList();
                if (await transaccion.YaRegistrada(solicitud.Empresa, ids).ConfigureAwait(false))
                {
                    return new ResultadoTerminarRecepcionDTO { Tipo = TIPO, Documento = solicitud.Documento, YaEstabaTerminada = true };
                }

                string diario = await transaccion.DiarioDeEntrada(solicitud.Empresa, solicitud.Almacen).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(diario))
                {
                    throw new NestoBusinessException($"El almacén {solicitud.Almacen} no tiene diario de entrada de reposiciones.");
                }
                List<int> enDiario = await transaccion.TraspasosEnDiario(solicitud.Empresa, solicitud.Almacen, diario).ConfigureAwait(false)
                    ?? new List<int>();
                if (!enDiario.Contains(traspaso))
                {
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
                // toca (ajustes en origen o el diario _ErrRepo, sin decidir): solo se informa
                List<FilaReposicion> enviado = await transaccion.LeerLineas(solicitud.Empresa, solicitud.Almacen, traspaso).ConfigureAwait(false)
                    ?? new List<FilaReposicion>();
                diferencias = Diferencias(enviado, solicitud.Lecturas);
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
                PendientesDeUbicarEntrada pendientesDeUbicar = await transaccion.LeerPendientesDeUbicar(solicitud.Empresa, diario).ConfigureAwait(false);
                await transaccion.Contabilizar(solicitud.Empresa, diario, solicitud.Usuario).ConfigureAwait(false);
                await transaccion.DevolverApartadas(solicitud.Empresa, diario, apartadas).ConfigureAwait(false);
                _ = await transaccion.DejarPendientesDeUbicar(solicitud.Empresa, pendientesDeUbicar, solicitud.Usuario).ConfigureAwait(false);
                await transaccion.RegistrarEvidencia(solicitud.Empresa, solicitud.Lecturas.Select(l => new EvidenciaRecepcion
                {
                    IdCliente = EvidenciasRecepcionSql.IdEvidencia(solicitud.IdRecepcion, l.Key),
                    IdRecepcion = solicitud.IdRecepcion,
                    NumeroOrigen = traspaso,
                    Producto = l.Key,
                    Cantidad = l.Value,
                    Usuario = solicitud.Usuario,
                    Dispositivo = solicitud.Dispositivo
                }).ToList()).ConfigureAwait(false);

                var avisos = new List<string>();
                if (solicitud.Ensayo != null)
                {
                    await solicitud.Ensayo.Acabar().ConfigureAwait(false);
                }
                return new ResultadoTerminarRecepcionDTO
                {
                    Tipo = TIPO,
                    Documento = traspaso.ToString(),
                    Avisos = avisos,
                    Diferencias = diferencias
                };
            }, solicitud.Ensayo != null).ConfigureAwait(false);

            if (resultado.YaEstabaTerminada || !diferencias.Any())
            {
                return resultado;
            }

            string destinatario = string.IsNullOrWhiteSpace(datos?.Creador) ? null : datos.Creador.Trim();
            string nombre = destinatario == null ? "el grupo " + Constantes.GruposSeguridad.ALMACEN
                : destinatario.Substring(destinatario.LastIndexOf('\\') + 1);
            resultado.Avisos = resultado.Avisos ?? new List<string>();
            if (solicitud.Ensayo != null)
            {
                resultado.Avisos.Add($"Lo leído no coincide con lo enviado: entraría lo leído y se avisaría a {nombre} (en un ensayo no se avisa a nadie).");
                return resultado;
            }
            resultado.Avisos.Add($"Lo leído no coincide con lo enviado: ha entrado lo leído y {nombre} está avisado de las diferencias.");
            resultado.AvisadoA = destinatario ?? Constantes.GruposSeguridad.ALMACEN;

            // Después de confirmar: un fallo al avisar no deshace la entrada
            string origen = string.IsNullOrWhiteSpace(datos?.Origen) ? "¿?" : datos.Origen.Trim();
            try
            {
                await avisador.Avisar(destinatario, $"Reposición {traspaso} ({origen} → {solicitud.Almacen}): lo recibido no coincide con lo enviado",
                    LineasAviso(diferencias, origen, solicitud)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Recepción reposiciones] No se pudo avisar de las diferencias de la reposición {traspaso}: {ex.Message}", ex));
            }
            return resultado;
        }

        internal const string HAZLA_EN_NESTO_VIEJO = "haz esta entrada en Nesto viejo o avisa a Andre.";

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

        private static List<string> LineasAviso(IEnumerable<DiferenciaPreparacionDTO> diferencias, string origen, SolicitudTerminarRecepcion solicitud)
        {
            List<string> lineas = diferencias.Select(d =>
                $"{d.Producto}: {(d.Ajeno ? "no venía" : $"enviado {d.Esperado}")}, leído {d.Leido}, diferencia {(d.Diferencia > 0 ? "+" : "")}{d.Diferencia}" +
                (string.IsNullOrWhiteSpace(d.Descripcion) ? "" : $" ({d.Descripcion})")).ToList();
            lineas.Add($"Ha entrado en {solicitud.Almacen} lo leído (lo ha recibido {solicitud.Usuario}). La salida de {origen} no se ha tocado.");
            return lineas;
        }
    }
}
