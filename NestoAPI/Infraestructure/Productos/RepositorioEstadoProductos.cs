using NestoAPI.Infraestructure.Stocks;
using NestoAPI.Infraestructure.Stocks.Tendencias;
using NestoAPI.Models;
using NestoAPI.Models.Productos;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Productos
{
    public class ResultadoAplicacionEstado
    {
        public int Cambiados { get; set; }
        public int ControlesQuitados { get; set; }
        public int ControlesCreados { get; set; }
    }

    /// <summary>NestoAPI#738 (sugerencia 793, Manuel): acceso a datos del cambio de estado de los productos (sustituible en los tests).</summary>
    public interface IRepositorioEstadoProductos
    {
        /// <summary>Los estados de la tabla EstadosProducto de la empresa (no está en el EDMX).</summary>
        Task<List<EstadoProductoDTO>> LeerEstados(string empresa);

        /// <summary>Las referencias (sin espacios) de los productos de la empresa que cumplen el filtro.</summary>
        Task<List<string>> ProductosDelFiltro(string empresa, FiltroEstadoProductos filtro);

        /// <summary>Estado, stock, pendientes y controles de stock de cada producto que exista (clave: referencia sin espacios).</summary>
        Task<Dictionary<string, DatosEstadoProducto>> Cargar(string empresa, IList<string> productos);

        /// <summary>Los controles de stock sugeridos por almacén para los productos que vuelven a Disponible.</summary>
        Task<Dictionary<string, List<ControlStockSugeridoDTO>>> SugerirControles(string empresa, IList<string> productos);

        /// <summary>Aplica los cambios (ninguno omitido) en una transacción: o todos o ninguno.</summary>
        Task<ResultadoAplicacionEstado> Aplicar(string empresa, IList<DecisionEstadoProducto> cambios, string usuario);
    }

    /// <summary>
    /// NestoAPI#738: <see cref="IRepositorioEstadoProductos"/> contra NV, en pocas consultas para todo el lote (no producto a
    /// producto), en tandas de <see cref="TAMANO_TANDA"/> referencias para no pasarse de parámetros en el IN.
    /// <para>trgProductosUpd (leído el 05/11/26): escribe la traza en LogModificaciones (columna ESTADO), manda el correo
    /// de «Productos pasados a estado NULO» / «recuperados de nulos» y NO encola en Nesto_sync cuando quien escribe es
    /// NUEVAVISION\RDS2016$, que es la cuenta con la que escribe la API: por eso aquí se encola a mano
    /// (<see cref="NVEntities.EncolarProductosSync"/>), como hacen Campañas, Familias o el Outlet. Su bloqueo «No se puede
    /// cambiar el estado porque hay controles de stock» hoy no salta nunca (exige Almacenes.StockMaximoDefecto &gt; 0 y
    /// todos están a 0), pero en la BD no hay ningún producto fuera de Disponible con máximo: la regla se cumple a mano, y
    /// ahora la cumple la API.</para>
    /// <para>trgControlesStockIns/Upd anotan en EntradasEstadoDistintoCero los máximos que se dan o suben a productos que NO
    /// están en Disponible: por eso el orden es quitar controles → cambiar el estado → crear controles.</para>
    /// </summary>
    public class RepositorioEstadoProductos : IRepositorioEstadoProductos
    {
        internal const int TAMANO_TANDA = 500;
        internal const int DIAS_STOCK_SEGURIDAD = 7;
        internal const string USUARIO_ENCOLADO = "Cambio de estado";

        private static readonly string[] SEDES = { Constantes.Almacenes.ALGETE, Constantes.Almacenes.REINA, Constantes.Almacenes.ALCOBENDAS };

        private readonly NVEntities db;
        private readonly IServicioPropuestaStockProducto servicioPropuesta;
        private readonly Action<Exception> registrarError;

        public RepositorioEstadoProductos(NVEntities db, IServicioPropuestaStockProducto servicioPropuesta = null, Action<Exception> registrarError = null)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
            this.servicioPropuesta = servicioPropuesta ?? new ServicioPropuestaStockProducto();
            this.registrarError = registrarError ?? ElmahHelper.Log;
        }

        public async Task<List<EstadoProductoDTO>> LeerEstados(string empresa)
        {
            List<FilaEstado> filas = await db.Database.SqlQuery<FilaEstado>(
                "SELECT Número AS Estado, RTRIM(ISNULL(Descripción, '')) AS Descripcion, ISNULL(AExtinguir, 0) AS AExtinguir, ISNULL(AAnular, 0) AS AAnular " +
                "FROM EstadosProducto WITH (NOLOCK) WHERE Empresa = @empresa ORDER BY Número",
                new SqlParameter("@empresa", empresa)).ToListAsync().ConfigureAwait(false);
            return filas.Select(f => new EstadoProductoDTO
            {
                Estado = f.Estado,
                Descripcion = f.Descripcion,
                AExtinguir = f.AExtinguir,
                AAnular = f.AAnular
            }).ToList();
        }

        public async Task<List<string>> ProductosDelFiltro(string empresa, FiltroEstadoProductos filtro)
        {
            IQueryable<Producto> consulta = db.Productos.Where(p => p.Empresa == empresa);
            string familia = filtro?.Familia?.Trim();
            string grupo = filtro?.Grupo?.Trim();
            string subgrupo = filtro?.Subgrupo?.Trim();
            string proveedor = filtro?.Proveedor?.Trim();
            if (!string.IsNullOrEmpty(familia))
            {
                consulta = consulta.Where(p => p.Familia == familia);
            }
            if (!string.IsNullOrEmpty(grupo))
            {
                consulta = consulta.Where(p => p.Grupo == grupo);
            }
            if (!string.IsNullOrEmpty(subgrupo))
            {
                consulta = consulta.Where(p => p.SubGrupo == subgrupo);
            }
            if (!string.IsNullOrEmpty(proveedor))
            {
                consulta = consulta.Where(p => db.ProveedoresProductoes.Any(pp => pp.Empresa == empresa && pp.Nº_Proveedor == proveedor && pp.Nº_Producto == p.Número));
            }
            List<string> lista = Limpiar(filtro?.Productos);
            if (lista.Count == 0)
            {
                return (await consulta.Select(p => p.Número).ToListAsync().ConfigureAwait(false))
                    .Select(n => n.Trim()).Distinct().ToList();
            }
            List<string> resultado = new List<string>();
            foreach (List<string> tanda in Tandas(lista))
            {
                resultado.AddRange(await consulta.Where(p => tanda.Contains(p.Número)).Select(p => p.Número).ToListAsync().ConfigureAwait(false));
            }
            // Los que no existen también vuelven (salen como «no existe» en la vista previa), en el orden en que se pidieron.
            HashSet<string> existentes = new HashSet<string>(resultado.Select(r => r.Trim()), StringComparer.OrdinalIgnoreCase);
            bool hayOtrosCriterios = !string.IsNullOrEmpty(familia) || !string.IsNullOrEmpty(grupo) || !string.IsNullOrEmpty(subgrupo) || !string.IsNullOrEmpty(proveedor);
            return hayOtrosCriterios ? lista.Where(existentes.Contains).ToList() : lista;
        }

        public async Task<Dictionary<string, DatosEstadoProducto>> Cargar(string empresa, IList<string> productos)
        {
            Dictionary<string, DatosEstadoProducto> datos = new Dictionary<string, DatosEstadoProducto>(StringComparer.OrdinalIgnoreCase);
            List<string> empresas = EmpresasDeStock(empresa);
            List<short> estadosPendientes = new List<short> { Constantes.EstadosLineaVenta.PENDIENTE, Constantes.EstadosLineaVenta.EN_CURSO };
            foreach (List<string> tanda in Tandas(Limpiar(productos)))
            {
                var fichas = await db.Productos.Where(p => p.Empresa == empresa && tanda.Contains(p.Número))
                    .Select(p => new { p.Número, p.Nombre, p.Estado }).ToListAsync().ConfigureAwait(false);
                foreach (var f in fichas)
                {
                    datos[f.Número.Trim()] = new DatosEstadoProducto { Producto = f.Número.Trim(), Nombre = f.Nombre?.Trim(), Estado = f.Estado };
                }

                var stocks = await db.ExtractosProducto
                    .Where(e => empresas.Contains(e.Empresa) && tanda.Contains(e.Número) && SEDES.Contains(e.Almacén))
                    .GroupBy(e => new { e.Número, e.Almacén })
                    .Select(g => new { g.Key.Número, g.Key.Almacén, Stock = g.Sum(e => (int)e.Cantidad) })
                    .ToListAsync().ConfigureAwait(false);
                foreach (var s in stocks)
                {
                    if (datos.TryGetValue(s.Número.Trim(), out DatosEstadoProducto d))
                    {
                        string almacen = s.Almacén.Trim();
                        d.Stock[almacen] = d.StockEn(almacen) + s.Stock;
                    }
                }

                // Lo pendiente de recibir, como CantidadPendienteRecibir de la ficha (NestoDTO): líneas de compra pendientes o en curso.
                var compras = await db.LinPedidoCmps
                    .Where(l => empresas.Contains(l.Empresa) && tanda.Contains(l.Producto) && estadosPendientes.Contains(l.Estado))
                    .GroupBy(l => l.Producto)
                    .Select(g => new { Producto = g.Key, Cantidad = g.Sum(l => (int?)l.Cantidad) ?? 0 })
                    .ToListAsync().ConfigureAwait(false);
                foreach (var c in compras)
                {
                    if (datos.TryGetValue(c.Producto.Trim(), out DatosEstadoProducto d))
                    {
                        d.PendienteRecibir += c.Cantidad;
                    }
                }

                // Pedidos de clientes pendientes de servir: líneas de producto pendientes o en curso (sin presupuestos).
                var pedidos = await db.LinPedidoVtas
                    .Where(l => empresas.Contains(l.Empresa) && tanda.Contains(l.Producto) && estadosPendientes.Contains(l.Estado)
                        && l.TipoLinea == Constantes.TiposLineaVenta.PRODUCTO)
                    .GroupBy(l => new { l.Producto, l.Número, l.Nº_Cliente })
                    .Select(g => new { g.Key.Producto, g.Key.Número, g.Key.Nº_Cliente, Cantidad = g.Sum(l => (int?)l.Cantidad) ?? 0 })
                    .ToListAsync().ConfigureAwait(false);
                foreach (var p in pedidos.OrderBy(p => p.Número))
                {
                    if (datos.TryGetValue(p.Producto.Trim(), out DatosEstadoProducto d))
                    {
                        d.PedidosPendientes.Add(new PedidoPendienteEstadoDTO { Pedido = p.Número, Cliente = p.Nº_Cliente?.Trim(), Cantidad = p.Cantidad });
                    }
                }

                var controles = await db.ControlesStocks.Where(c => c.Empresa == empresa && tanda.Contains(c.Número))
                    .Select(c => new { c.Número, c.Almacén, c.StockMínimo, c.StockMáximo })
                    .ToListAsync().ConfigureAwait(false);
                foreach (var c in controles)
                {
                    if (datos.TryGetValue(c.Número.Trim(), out DatosEstadoProducto d))
                    {
                        d.Controles.Add(new ControlStockActual { Almacen = c.Almacén.Trim(), Minimo = c.StockMínimo, Maximo = c.StockMáximo });
                    }
                }
            }
            return datos;
        }

        /// <summary>
        /// Lo mismo que sugiere la ficha de controles de stock (GET api/ControlesStock, NestoAPI#628): en ALG, el motor de las
        /// propuestas de stock (como en el Excel), aquí calculado como si el producto ya estuviera en Disponible; si el producto
        /// no entra en el motor o el motor falla, la fórmula de siempre. En REI y ALC, la media de 24 meses × (7 + 2 × días del
        /// proveedor), con mínimo 0 (NestoAPI#667).
        /// </summary>
        public async Task<Dictionary<string, List<ControlStockSugeridoDTO>>> SugerirControles(string empresa, IList<string> productos)
        {
            Dictionary<string, List<ControlStockSugeridoDTO>> resultado = new Dictionary<string, List<ControlStockSugeridoDTO>>(StringComparer.OrdinalIgnoreCase);
            List<string> lista = Limpiar(productos);
            if (lista.Count == 0)
            {
                return resultado;
            }

            Dictionary<string, ResultadoTendencia> motor;
            try
            {
                motor = empresa?.Trim() == Constantes.Empresas.EMPRESA_POR_DEFECTO
                    ? await Task.Run(() => servicioPropuesta.CalcularComoDisponibles(lista)).ConfigureAwait(false)
                    : new Dictionary<string, ResultadoTendencia>();
            }
            catch (Exception ex)
            {
                registrarError(new Exception($"[NestoAPI#738] No se han podido calcular las propuestas de stock de {lista.Count} productos que vuelven a " +
                    $"Disponible; en ALG se sugiere con la fórmula de siempre: {ex.Message}", ex));
                motor = new Dictionary<string, ResultadoTendencia>();
            }

            DateTime haceDosAnnos = DateTime.Today.AddMonths(-24);
            Dictionary<string, int> diasProveedor = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, int> consumo = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, DateTime> primeraEntrada = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            foreach (List<string> tanda in Tandas(lista))
            {
                var proveedores = await db.ProveedoresProductoes
                    .Where(pp => pp.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && tanda.Contains(pp.Nº_Producto))
                    .Select(pp => new { pp.Nº_Producto, pp.Orden, Dias = pp.Proveedore.DíasEnServir })
                    .ToListAsync().ConfigureAwait(false);
                foreach (var g in proveedores.GroupBy(pp => pp.Nº_Producto.Trim()))
                {
                    diasProveedor[g.Key] = g.OrderBy(pp => pp.Orden).First().Dias;
                }

                var ventas = await db.LinPedidoVtas
                    .Where(l => tanda.Contains(l.Producto) && l.Fecha_Factura > haceDosAnnos && SEDES.Contains(l.Almacén))
                    .GroupBy(l => new { l.Producto, l.Almacén })
                    .Select(g => new { g.Key.Producto, g.Key.Almacén, Cantidad = g.Sum(l => (int?)l.Cantidad) ?? 0 })
                    .ToListAsync().ConfigureAwait(false);
                foreach (var v in ventas)
                {
                    consumo[Clave(v.Producto, v.Almacén)] = v.Cantidad;
                }

                var entradas = await db.ExtractosProducto
                    .Where(e => tanda.Contains(e.Número) && e.Cantidad > 0 && SEDES.Contains(e.Almacén))
                    .GroupBy(e => new { e.Número, e.Almacén })
                    .Select(g => new { g.Key.Número, g.Key.Almacén, Fecha = g.Min(e => e.Fecha) })
                    .ToListAsync().ConfigureAwait(false);
                foreach (var e in entradas)
                {
                    primeraEntrada[Clave(e.Número, e.Almacén)] = e.Fecha;
                }
            }

            foreach (string producto in lista)
            {
                int dias = diasProveedor.TryGetValue(producto, out int d) ? d : 0;
                List<ControlStockSugeridoDTO> sugeridos = new List<ControlStockSugeridoDTO>();
                foreach (string almacen in SEDES)
                {
                    ControlStockAlmacenModel clasico = new ControlStockAlmacenModel
                    {
                        Almacen = almacen,
                        DiasReaprovisionamiento = dias,
                        DiasStockSeguridad = DIAS_STOCK_SEGURIDAD,
                        ConsumoAnual = consumo.TryGetValue(Clave(producto, almacen), out int c) ? c : 0,
                        MesesAntiguedad = Meses(primeraEntrada.TryGetValue(Clave(producto, almacen), out DateTime f) ? f : (DateTime?)null)
                    };
                    if (almacen == Constantes.Almacenes.ALGETE)
                    {
                        if (motor.TryGetValue(producto, out ResultadoTendencia r) && r != null)
                        {
                            sugeridos.Add(new ControlStockSugeridoDTO
                            {
                                Almacen = almacen,
                                StockMaximo = Math.Max(0, r.StockMaximoPropuesto ?? 0),
                                StockMinimo = Math.Max(0, r.StockMinimoPropuesto ?? 0),
                                Explicacion = r.Explicacion
                            });
                            continue;
                        }
                        // Fuera del motor (o si ha fallado): la fórmula de siempre, con el mínimo de toda la empresa, como la ficha.
                        ControlStockProductoModel empresaEntera = new ControlStockProductoModel
                        {
                            DiasReaprovisionamiento = dias,
                            DiasStockSeguridad = DIAS_STOCK_SEGURIDAD,
                            ConsumoAnual = SEDES.Sum(a => consumo.TryGetValue(Clave(producto, a), out int ca) ? ca : 0),
                            MesesAntiguedad = Meses(SEDES.Select(a => primeraEntrada.TryGetValue(Clave(producto, a), out DateTime fa) ? fa : (DateTime?)null)
                                .Min())
                        };
                        sugeridos.Add(new ControlStockSugeridoDTO
                        {
                            Almacen = almacen,
                            StockMaximo = clasico.StockMaximoCalculado,
                            StockMinimo = Math.Max(0, empresaEntera.StockMinimoCalculado),
                            Explicacion = $"Fórmula de siempre. {clasico.ExplicacionMaximoClasico(almacen)} {empresaEntera.ExplicacionMinimoClasico()}"
                        });
                        continue;
                    }
                    sugeridos.Add(new ControlStockSugeridoDTO
                    {
                        Almacen = almacen,
                        StockMaximo = clasico.StockMaximoCalculado,
                        StockMinimo = 0,
                        Explicacion = clasico.ExplicacionMaximoClasico(almacen)
                    });
                }
                resultado[producto] = sugeridos;
            }
            return resultado;
        }

        public async Task<ResultadoAplicacionEstado> Aplicar(string empresa, IList<DecisionEstadoProducto> cambios, string usuario)
        {
            ResultadoAplicacionEstado resultado = new ResultadoAplicacionEstado();
            List<DecisionEstadoProducto> aplicar = (cambios ?? new List<DecisionEstadoProducto>()).Where(c => !c.Omitir).ToList();
            if (aplicar.Count == 0)
            {
                return resultado;
            }
            string usuarioAuditoria = UsuarioAuditoriaHelper.ParaAuditoria(usuario);
            DateTime ahora = DateTime.Now;
            List<string> referencias = aplicar.Select(c => c.Producto).ToList();

            using (DbContextTransaction transaccion = db.Database.BeginTransaction())
            {
                List<Producto> productos = new List<Producto>();
                List<ControlStock> controles = new List<ControlStock>();
                foreach (List<string> tanda in Tandas(referencias))
                {
                    productos.AddRange(await db.Productos.Where(p => p.Empresa == empresa && tanda.Contains(p.Número)).ToListAsync().ConfigureAwait(false));
                    controles.AddRange(await db.ControlesStocks.Where(c => c.Empresa == empresa && tanda.Contains(c.Número)).ToListAsync().ConfigureAwait(false));
                }
                Dictionary<string, Producto> porReferencia = productos.ToDictionary(p => p.Número.Trim(), StringComparer.OrdinalIgnoreCase);

                // 1. Quitar los controles de stock de los que salen de Disponible (máximo y mínimo a 0; la fila se queda, como
                //    las 8.400 que ya hay a 0, con sus múltiplos y su categoría).
                foreach (DecisionEstadoProducto cambio in aplicar)
                {
                    foreach (string almacen in cambio.ControlesAQuitar)
                    {
                        foreach (ControlStock control in controles.Where(c => Igual(c.Número, cambio.Producto) && Igual(c.Almacén, almacen)))
                        {
                            ControlesStockEscritura.CambiarMaximoMinimo(control, 0, 0, usuarioAuditoria, ahora);
                            resultado.ControlesQuitados++;
                        }
                    }
                }
                _ = await db.SaveChangesAsync().ConfigureAwait(false);

                // 2. El estado, con su traza en Modificaciones con el usuario real (LogModificaciones la escribe el trigger,
                //    pero con la cuenta de la API).
                foreach (DecisionEstadoProducto cambio in aplicar)
                {
                    if (!porReferencia.TryGetValue(cambio.Producto, out Producto producto))
                    {
                        continue;
                    }
                    short? anterior = producto.Estado;
                    producto.Estado = cambio.EstadoNuevo;
                    producto.Usuario = usuarioAuditoria;
                    producto.Fecha_Modificación = ahora;
                    string detalle = cambio.ControlesAQuitar.Count > 0
                        ? $"; controles de stock quitados en {ReglasEstadoProducto.Lista(cambio.ControlesAQuitar)}"
                        : cambio.ControlesACrear.Count > 0
                            ? $"; controles de stock sugeridos: {string.Join(", ", cambio.ControlesACrear.Select(c => $"{c.Almacen} {c.StockMaximo}/{c.StockMinimo}"))}"
                            : string.Empty;
                    _ = db.Modificaciones.Add(new Modificacion
                    {
                        Tabla = "Productos",
                        Anterior = $"Producto {empresa?.Trim()}/{cambio.Producto} Estado={(anterior.HasValue ? anterior.Value.ToString() : "NULL")}",
                        Nuevo = $"Producto {empresa?.Trim()}/{cambio.Producto} Estado={cambio.EstadoNuevo} (NestoAPI#738 cambio de estado{detalle}; usuario {usuario})",
                        Usuario = usuarioAuditoria,
                        Fecha = ahora
                    });
                    resultado.Cambiados++;
                }
                _ = await db.SaveChangesAsync().ConfigureAwait(false);

                // 3. Los controles de los que vuelven a Disponible, ya en estado 0.
                foreach (DecisionEstadoProducto cambio in aplicar)
                {
                    foreach (ControlStockSugeridoDTO sugerido in cambio.ControlesACrear)
                    {
                        short maximo = (short)Math.Min(sugerido.StockMaximo, short.MaxValue);
                        short minimo = (short)Math.Min(sugerido.StockMinimo, short.MaxValue);
                        ControlStock existente = controles.FirstOrDefault(c => Igual(c.Número, cambio.Producto) && Igual(c.Almacén, sugerido.Almacen));
                        if (existente != null)
                        {
                            ControlesStockEscritura.CambiarMaximoMinimo(existente, minimo, maximo, usuarioAuditoria, ahora);
                        }
                        else
                        {
                            _ = db.ControlesStocks.Add(ControlesStockEscritura.Nuevo(empresa, sugerido.Almacen, cambio.Producto, minimo, maximo, 1, usuarioAuditoria, ahora));
                        }
                        resultado.ControlesCreados++;
                    }
                }
                _ = await db.SaveChangesAsync().ConfigureAwait(false);

                // 4. La tienda (y Odoo) se enteran por Nesto_sync, como siempre: el trigger no encola lo que escribe la API.
                if (empresa?.Trim() == Constantes.Empresas.EMPRESA_POR_DEFECTO)
                {
                    _ = await db.EncolarProductosSync(referencias, $"{USUARIO_ENCOLADO} ({usuario})").ConfigureAwait(false);
                }
                transaccion.Commit();
            }
            return resultado;
        }

        /// <summary>El stock y los pendientes de la empresa 1 incluyen los de su espejo (la 3), como en la ficha.</summary>
        internal static List<string> EmpresasDeStock(string empresa)
        {
            string limpia = empresa?.Trim();
            return limpia == Constantes.Empresas.EMPRESA_POR_DEFECTO
                ? new List<string> { Constantes.Empresas.EMPRESA_POR_DEFECTO, Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO }
                : new List<string> { limpia };
        }

        internal static List<string> Limpiar(IEnumerable<string> productos)
        {
            return (productos ?? Enumerable.Empty<string>())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static IEnumerable<List<string>> Tandas(List<string> lista)
        {
            for (int i = 0; i < lista.Count; i += TAMANO_TANDA)
            {
                yield return lista.Skip(i).Take(TAMANO_TANDA).ToList();
            }
        }

        private static decimal Meses(DateTime? primeraEntrada)
        {
            DateTime desde = primeraEntrada ?? DateTime.Now;
            return (decimal)(DateTime.Now - desde).TotalDays / 30;
        }

        private static string Clave(string producto, string almacen) => $"{producto?.Trim()}|{almacen?.Trim()}";

        private static bool Igual(string a, string b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

        private class FilaEstado
        {
            public short Estado { get; set; }
            public string Descripcion { get; set; }
            public bool AExtinguir { get; set; }
            public bool AAnular { get; set; }
        }
    }
}
