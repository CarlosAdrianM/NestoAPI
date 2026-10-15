using NestoAPI.Infraestructure.Sincronizacion;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Outlet
{
    /// <summary>NestoAPI#643: acceso a datos del cuadre del Outlet (sustituible en los tests).</summary>
    public interface IRepositorioOutlet
    {
        /// <summary>
        /// Los productos que importan al Outlet: los de estado 4 con ficha web (candidatos a estar) y los que
        /// tienen hoy alguna categoría Outlet (candidatos a salir), con la puerta de publicación ya evaluada.
        /// </summary>
        Task<List<SituacionOutletProducto>> CargarSituacion();

        /// <summary>Aplica el cambio de UN producto en ProductosCategoriasSecundarias, en una transacción.</summary>
        Task AplicarCambio(CambioOutlet cambio, string usuario);

        /// <summary>Encola los productos en Nesto_sync para que la pasada de los 5 minutos los republique.</summary>
        Task<int> EncolarRepublicacion(IEnumerable<string> productos, string usuario);
    }

    /// <summary>
    /// NestoAPI#643: <see cref="IRepositorioOutlet"/> contra NV. La carga va en pocas consultas para todos los
    /// productos a la vez (no producto a producto) y evalúa la puerta con su núcleo puro
    /// (<see cref="PuertaPublicacionTienda.Evaluar"/>), con los mismos datos que carga
    /// <see cref="PuertaPublicacionTienda.CargarDatos"/>: si la puerta cambia sus datos, cambiar también esta carga.
    /// ProductosCategoriasSecundarias no tiene triggers (comprobado 15/10/26) y el API ya tiene permiso de escritura
    /// (Scripts/Issue414_ProductosCategoriasSecundarias.sql).
    /// </summary>
    public class RepositorioOutletSql : IRepositorioOutlet
    {
        private const string EMPRESA = Constantes.Empresas.EMPRESA_POR_DEFECTO;

        public async Task<List<SituacionOutletProducto>> CargarSituacion()
        {
            using (NVEntities db = new NVEntities())
            {
                db.Configuration.LazyLoadingEnabled = false;
                db.Configuration.ProxyCreationEnabled = false;
                db.Database.CommandTimeout = 300;

                var categorias = await db.ProductosCategoriasSecundarias
                    .Where(c => c.Empresa == EMPRESA
                        && ((c.Grupo == "PEL" && c.SubGrupo == "OUT") || (c.Grupo == "COS" && (c.SubGrupo == "OUT" || c.SubGrupo == "OUM"))))
                    .Select(c => new { c.Número, c.Grupo, c.SubGrupo })
                    .ToListAsync().ConfigureAwait(false);

                var productos = await db.Productos
                    .Where(p => p.Empresa == EMPRESA
                        && ((p.Estado == ReglasOutlet.ESTADO_OUTLET && db.PrestashopProductos.Any(pp => pp.Empresa == p.Empresa && pp.Número == p.Número))
                            || db.ProductosCategoriasSecundarias.Any(c => c.Empresa == p.Empresa && c.Número == p.Número
                                && ((c.Grupo == "PEL" && c.SubGrupo == "OUT") || (c.Grupo == "COS" && (c.SubGrupo == "OUT" || c.SubGrupo == "OUM"))))))
                    .Select(p => new
                    {
                        p.Número,
                        p.Nombre,
                        p.Grupo,
                        p.SubGrupo,
                        p.Familia,
                        p.Estado,
                        p.PVP,
                        p.Ficticio,
                        DescripcionFamilia = p.Familia1.Descripción,
                        SoloVentaPresencial = (bool?)p.Familia1.SoloVentaPresencial,
                        DescripcionSubgrupo = p.SubGruposProducto.Descripción,
                        TieneFichaWeb = db.PrestashopProductos.Any(pp => pp.Empresa == p.Empresa && pp.Número == p.Número),
                        TieneProveedores = db.ProveedoresProductoes.Any(r => r.Empresa == p.Empresa && r.Nº_Producto == p.Número),
                        TieneProveedorOrdenUno = db.ProveedoresProductoes.Any(r => r.Empresa == p.Empresa && r.Nº_Producto == p.Número && r.Orden == 1),
                        EsParteDeKitActivo = db.Kits.Any(k => k.Empresa == p.Empresa && k.NúmeroAsociado == p.Número
                            && db.Productos.Any(t => t.Empresa == k.Empresa && t.Número == k.Número && t.Estado == 0))
                    })
                    .ToListAsync().ConfigureAwait(false);

                // El extracto solo hace falta para los recambios de aparatología (APA/010) que la puerta evalúa.
                List<string> recambios = productos
                    .Where(p => p.TieneFichaWeb && p.Estado == ReglasOutlet.ESTADO_OUTLET
                        && string.Equals(p.Grupo?.Trim(), "APA", StringComparison.OrdinalIgnoreCase)
                        && string.Equals(p.SubGrupo?.Trim(), "010", StringComparison.OrdinalIgnoreCase))
                    .Select(p => p.Número)
                    .ToList();
                HashSet<string> conMovimiento = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (recambios.Any())
                {
                    DateTime haceTresAnnos = DateTime.Today.AddYears(-3);
                    List<string> movidos = await db.ExtractosProducto
                        .Where(e => recambios.Contains(e.Número) && (e.Empresa == "1" || e.Empresa == "3") && e.Fecha >= haceTresAnnos)
                        .Select(e => e.Número)
                        .Distinct()
                        .ToListAsync().ConfigureAwait(false);
                    foreach (string n in movidos)
                    {
                        _ = conMovimiento.Add(n.Trim());
                    }
                }

                ILookup<string, CategoriaOutlet> categoriasPorProducto = categorias
                    .ToLookup(c => c.Número.Trim(), c => new CategoriaOutlet(c.Grupo, c.SubGrupo), StringComparer.OrdinalIgnoreCase);

                List<SituacionOutletProducto> resultado = new List<SituacionOutletProducto>();
                foreach (var p in productos)
                {
                    string numero = p.Número.Trim();
                    SituacionOutletProducto situacion = new SituacionOutletProducto
                    {
                        Numero = numero,
                        Nombre = p.Nombre?.Trim(),
                        Grupo = p.Grupo?.Trim(),
                        Subgrupo = p.SubGrupo?.Trim(),
                        Estado = p.Estado ?? 0,
                        TieneFichaWeb = p.TieneFichaWeb,
                        CategoriasOutletActuales = categoriasPorProducto[numero].ToList()
                    };

                    // La puerta solo decide algo para los de estado 4 con ficha: los demás salen (o no entran)
                    // sin necesidad de evaluarla.
                    if (situacion.Estado == ReglasOutlet.ESTADO_OUTLET && situacion.TieneFichaWeb)
                    {
                        ResultadoPuertaPublicacion puerta;
                        if (!p.PVP.HasValue || !p.Estado.HasValue)
                        {
                            // Lo mismo que SincronizacionJobsService.TieneDatosMinimosParaSincronizar.
                            puerta = ResultadoPuertaPublicacion.NoPublicable("sin PVP o sin Estado (referencia reservada)");
                        }
                        else
                        {
                            puerta = PuertaPublicacionTienda.Evaluar(new DatosPuertaPublicacion
                            {
                                Numero = numero,
                                Grupo = p.Grupo?.Trim(),
                                Subgrupo = p.SubGrupo?.Trim(),
                                FamiliaCodigo = p.Familia?.Trim(),
                                DescripcionFamilia = p.DescripcionFamilia?.Trim(),
                                DescripcionSubgrupo = p.DescripcionSubgrupo?.Trim(),
                                Ficticio = p.Ficticio,
                                Estado = p.Estado ?? 0,
                                FamiliaSoloVentaPresencial = p.SoloVentaPresencial == true,
                                Nombre = p.Nombre?.Trim(),
                                TieneProveedorPrincipalValido = !p.TieneProveedores || p.TieneProveedorOrdenUno,
                                EsParteDeKitActivo = p.EsParteDeKitActivo,
                                TieneMovimientoExtractoTresAnnos = conMovimiento.Contains(numero)
                            });
                        }
                        situacion.Publicable = puerta.Publicable;
                        situacion.MotivoNoPublicable = puerta.Motivo;
                    }
                    resultado.Add(situacion);
                }
                return resultado;
            }
        }

        public async Task AplicarCambio(CambioOutlet cambio, string usuario)
        {
            if (cambio == null || string.IsNullOrWhiteSpace(cambio.Numero))
            {
                return;
            }
            using (NVEntities db = new NVEntities())
            {
                // Un producto = una sentencia = una transacción (ExecuteSqlCommand la abre por defecto): quita las
                // categorías Outlet que no le tocan y pone la suya (si le falta) con el siguiente Orden libre. Las
                // demás categorías secundarias del producto no se tocan. La UNIQUE (Empresa, Número, Grupo, SubGrupo)
                // impide duplicar aunque dos pasadas coincidieran.
                object grupo = (object)cambio.CategoriaPoner?.Grupo ?? DBNull.Value;
                object subgrupo = (object)cambio.CategoriaPoner?.Subgrupo ?? DBNull.Value;
                _ = await db.Database.ExecuteSqlCommandAsync(@"
                    SET XACT_ABORT ON;
                    DELETE FROM ProductosCategoriasSecundarias
                     WHERE Empresa = @empresa AND [Número] = @numero
                       AND ((Grupo = 'PEL' AND SubGrupo = 'OUT') OR (Grupo = 'COS' AND SubGrupo IN ('OUT', 'OUM')))
                       AND NOT (Grupo = ISNULL(@grupo, '') AND SubGrupo = ISNULL(@subgrupo, ''));
                    IF @grupo IS NOT NULL AND NOT EXISTS (SELECT 1 FROM ProductosCategoriasSecundarias
                                                           WHERE Empresa = @empresa AND [Número] = @numero
                                                             AND Grupo = @grupo AND SubGrupo = @subgrupo)
                        INSERT INTO ProductosCategoriasSecundarias (Empresa, [Número], Orden, Grupo, SubGrupo, Usuario, [Fecha Modificación])
                        SELECT @empresa, @numero, ISNULL(MAX(Orden), 0) + 1, @grupo, @subgrupo, @usuario, GETDATE()
                          FROM ProductosCategoriasSecundarias WITH (UPDLOCK, HOLDLOCK)
                         WHERE Empresa = @empresa AND [Número] = @numero;",
                    new SqlParameter("@empresa", EMPRESA),
                    new SqlParameter("@numero", cambio.Numero.Trim()),
                    new SqlParameter("@grupo", grupo),
                    new SqlParameter("@subgrupo", subgrupo),
                    new SqlParameter("@usuario", usuario ?? ServicioOutlet.USUARIO)).ConfigureAwait(false);
            }
        }

        public async Task<int> EncolarRepublicacion(IEnumerable<string> productos, string usuario)
        {
            using (NVEntities db = new NVEntities())
            {
                return await db.EncolarProductosSync(productos, usuario).ConfigureAwait(false);
            }
        }
    }
}
