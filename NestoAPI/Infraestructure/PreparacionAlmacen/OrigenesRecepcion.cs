using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infrastructure;
using NestoAPI.Models;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Linq;
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
    }

    /// <summary>Avisar a los de Compras (buzón y campana de Nesto). Nunca debe romper la recepción.</summary>
    public interface IAvisadorCompras
    {
        Task Avisar(string titulo, IEnumerable<string> avisos);
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

                return new ResultadoTerminarRecepcionDTO
                {
                    Tipo = TIPO,
                    Documento = solicitud.Documento,
                    Documentos = documentos,
                    NoEsperados = plan.NoPedidos
                        .Select(n => new DiferenciaPreparacionDTO { Producto = n.Producto, Leido = n.Cantidad, Ajeno = true })
                        .ToList(),
                    Avisos = plan.AvisosParaCompras.ToList(),
                    Recuperadas = plan.Recuperadas.Select(r => new LineaRecuperadaDTO
                    {
                        Pedido = r.Pedido,
                        Producto = r.Producto,
                        Cantidad = r.Cantidad,
                        FechaNoServido = r.FechaNoServido,
                        Texto = r.Texto
                    }).ToList()
                };
            }).ConfigureAwait(false);

            // Después de confirmar: un fallo al avisar no deshace la recepción
            if (plan != null && plan.AvisosParaCompras.Any() && !resultado.YaEstabaTerminada)
            {
                try
                {
                    await avisador.Avisar($"Recepción del proveedor {solicitud.Documento} ({solicitud.Usuario})", plan.AvisosParaCompras)
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
    /// <para>Quién: hoy la entrada la da siempre alguien cuyo AlmacénPedidoVta es el almacén de destino (60 días: Reina →
    /// REI, Paloma → ALC, Andre/Alfredo/Santiago → ALG). Se exige lo mismo.</para>
    /// </summary>
    public class OrigenRecepcionReposiciones : IOrigenRecepcion
    {
        public const string TIPO = "REPO";
        private const string CLAVE_ALMACEN_USUARIO = "AlmacénPedidoVta";

        private readonly IServicioRecepcionReposiciones reposiciones;
        private readonly IRepositorioCierreReposiciones cierre;
        private readonly Func<string, string, string> almacenDelUsuario;

        public OrigenRecepcionReposiciones(IServicioRecepcionReposiciones reposiciones, IRepositorioCierreReposiciones cierre)
            : this(reposiciones, cierre, null)
        {
        }

        /// <param name="almacenDelUsuario">(empresa, usuario sin dominio) → su AlmacénPedidoVta.</param>
        internal OrigenRecepcionReposiciones(IServicioRecepcionReposiciones reposiciones, IRepositorioCierreReposiciones cierre,
            Func<string, string, string> almacenDelUsuario)
        {
            this.reposiciones = reposiciones;
            this.cierre = cierre;
            this.almacenDelUsuario = almacenDelUsuario
                ?? ((empresa, usuario) => Controllers.ParametrosUsuarioController.LeerParametro(empresa, usuario, CLAVE_ALMACEN_USUARIO));
        }

        public string Tipo => TIPO;
        public bool SeTerminaDesdeAqui => true;

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
                Titulo = $"Reposición {r.Traspaso}",
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
            return recepcion == null ? null : new RecepcionDTO
            {
                Tipo = TIPO,
                Documento = traspaso.ToString(),
                Titulo = $"Reposición {traspaso}",
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

        public Task<ResultadoTerminarRecepcionDTO> Terminar(SolicitudTerminarRecepcion solicitud)
        {
            if (!int.TryParse(solicitud.Documento?.Trim(), out int traspaso))
            {
                throw new NestoBusinessException($"«{solicitud.Documento}» no es un número de traspaso.");
            }
            return cierre.EnTransaccion(async transaccion =>
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

                await transaccion.Contabilizar(solicitud.Empresa, diario, solicitud.Usuario).ConfigureAwait(false);
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
                List<int> otras = enDiario.Where(t => t != traspaso).Distinct().OrderBy(t => t).ToList();
                if (otras.Any())
                {
                    avisos.Add($"También ha entrado {(otras.Count == 1 ? "la reposición" : "las reposiciones")} {string.Join(", ", otras)}: " +
                        "van en el mismo diario de entrada y se contabiliza entero, como en Nesto.");
                }
                return new ResultadoTerminarRecepcionDTO { Tipo = TIPO, Documento = traspaso.ToString(), Avisos = avisos };
            });
        }
    }
}
