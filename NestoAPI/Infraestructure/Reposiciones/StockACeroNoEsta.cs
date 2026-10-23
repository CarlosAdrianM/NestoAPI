using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Reposiciones
{
    // ------------------------------------------------------------------------------------------------------------------
    // Sugerencia 753 de Novedades (Paloma, Alcobendas). Decisión de Carlos (23/10/26): en un almacén CON ubicaciones (Algete)
    // «No está» dice que no está en ese hueco: solo se toca el hueco (como siempre). En un almacén SIN ubicaciones (Reina,
    // Alcobendas: Almacenes.ControlUbicaciones = 0) no hay hueco: «No está» quiere decir que no hay ninguno, así que, al
    // TERMINAR la reposición (no al dar a «No está»), el stock de ese producto en ese almacén se pone a 0.
    //
    //  - Quién: ServicioPreparacionReposicion.Terminar (POST api/Reposiciones/EnPreparacion/Terminar, el «Enviar
    //    reposición» de las tiendas), con los productos que Ariadna manda en { NoEsta }. Las tiendas contabilizan la salida
    //    ahí mismo; el núcleo de salidas (ServicioSalidas, fase PICK) solo lo usan las reposiciones que salen de Algete.
    //  - Después de confirmar la reposición, cada producto en su transacción: un fallo nunca la deshace (ELMAH + aviso).
    //  - Por la única puerta a ExtractoProducto (prdExtrProducto), en el diario «_ErrRepo», con el mismo movimiento que el
    //    ajuste de #634 (TransaccionDiferenciasReposicionSql.Mover), fecha de ahora (el procedimiento rechaza apuntes
    //    anteriores a un inventario en curso) y el NºTraspaso de la reposición.
    //  - Cantidad = −(stock que queda en el almacén después de la salida). Con stock 0 o NEGATIVO no se apunta nada: «No
    //    está» dice que no hay, no que haya; subir un negativo a 0 sería dar entrada a unidades que nadie ha visto, y un
    //    negativo es otro descuadre (una entrada sin contabilizar, una diferencia pendiente) que se arregla por su lado.
    //  - Interruptor Reposiciones:NoEstaStockACero (Apagado / Activo). Sin la clave, Apagado.
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// INTERRUPTOR <c>Reposiciones:NoEstaStockACero</c> en Web.config: «Activo» lo enciende; cualquier otra cosa (o sin la
    /// clave), Apagado. OJO: el valor del Web.config del repo es el que se publica.
    /// </summary>
    public static class InterruptorNoEstaStockACero
    {
        internal const string CLAVE = "Reposiciones:NoEstaStockACero";
        internal const string ACTIVO = "Activo";

        public static bool EstaActivo()
        {
            return string.Equals(ConfigurationManager.AppSettings[CLAVE]?.Trim(), ACTIVO, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>El almacén de origen de la salida, tal como lo necesita la regularización.</summary>
    public class AlmacenStockACero
    {
        public string Nombre { get; set; }
        public bool ControlUbicaciones { get; set; }
    }

    /// <summary>Un producto cuyo stock se ha puesto a 0.</summary>
    public class ProductoStockACero
    {
        public string Producto { get; set; }
        /// <summary>El stock que tenía en el almacén justo antes del apunte (lo que se ha quitado).</summary>
        public int Tenia { get; set; }
        public int? NumeroOrdenExtracto { get; set; }
    }

    /// <summary>Lo que ha pasado al poner a 0 el stock de lo que «No está».</summary>
    public class ResultadoStockACero
    {
        public List<ProductoStockACero> Puestos { get; set; } = new List<ProductoStockACero>();
        /// <summary>Las frases para Cambios (la primera, el resumen «Stock de Alcobendas a 0: …»).</summary>
        public List<string> Cambios { get; set; } = new List<string>();
        /// <summary>Lo que no se ha podido hacer (null si ha ido bien): va también al mensaje.</summary>
        public string Aviso { get; set; }
    }

    public interface ITransaccionStockACero
    {
        /// <summary>Null si el almacén no existe.</summary>
        Task<AlmacenStockACero> LeerAlmacen(string empresa, string almacen);
        /// <summary>SUM(ExtractoProducto.Cantidad) del producto en el almacén, bloqueado hasta el final de la transacción.</summary>
        Task<int> LeerStock(string empresa, string almacen, string producto);
        /// <summary>El mismo movimiento que el ajuste de #634: prdExtrProducto en el diario «_ErrRepo».</summary>
        Task<ResultadoMovimientoStock> Mover(MovimientoStockDiferencia movimiento);
    }

    public interface IRepositorioStockACero
    {
        /// <summary>Todo o nada.</summary>
        Task<T> EnTransaccion<T>(Func<ITransaccionStockACero, Task<T>> trabajo);
    }

    public interface IServicioStockACeroNoEsta
    {
        /// <summary>
        /// Después de confirmar la reposición <paramref name="traspaso"/> desde <paramref name="almacen"/>: si el almacén no
        /// tiene control de ubicaciones, el stock de cada producto dado por «No está» a 0. Nunca lanza.
        /// </summary>
        Task<ResultadoStockACero> PonerACero(string empresa, string almacen, int traspaso, IEnumerable<string> productos, string usuario);
    }

    public class ServicioStockACeroNoEsta : IServicioStockACeroNoEsta
    {
        /// <summary>PreExtrProducto.Texto es char(50).</summary>
        internal const int LONGITUD_TEXTO = 50;

        private readonly Func<IRepositorioStockACero> crearRepositorio;
        private readonly Func<bool> activo;

        /// <summary>El de la BD: un contexto propio por llamada (va después de la transacción de la salida).</summary>
        public ServicioStockACeroNoEsta() : this(null, null)
        {
        }

        /// <param name="crearRepositorio">Null: <see cref="RepositorioStockACeroSql"/> con su contexto (se libera al terminar).</param>
        /// <param name="activo">Null: <see cref="InterruptorNoEstaStockACero"/>.</param>
        internal ServicioStockACeroNoEsta(Func<IRepositorioStockACero> crearRepositorio, Func<bool> activo)
        {
            this.crearRepositorio = crearRepositorio ?? RepositorioStockACeroSql.ConContextoPropio;
            this.activo = activo ?? InterruptorNoEstaStockACero.EstaActivo;
        }

        public async Task<ResultadoStockACero> PonerACero(string empresa, string almacen, int traspaso, IEnumerable<string> productos,
            string usuario)
        {
            var resultado = new ResultadoStockACero();
            List<string> lista = (productos ?? Enumerable.Empty<string>())
                .Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (lista.Count == 0 || string.IsNullOrWhiteSpace(almacen) || !activo())
            {
                return resultado;
            }
            almacen = almacen.Trim();
            IRepositorioStockACero repositorio = null;
            try
            {
                repositorio = crearRepositorio();
                AlmacenStockACero datos = await repositorio.EnTransaccion(t => t.LeerAlmacen(empresa, almacen)).ConfigureAwait(false);
                if (datos == null || datos.ControlUbicaciones)
                {
                    return resultado; // con ubicaciones, «No está» es del hueco: el stock no se toca
                }
                string nombre = string.IsNullOrWhiteSpace(datos.Nombre) ? almacen : datos.Nombre.Trim();

                var fallidos = new List<string>();
                foreach (string producto in lista)
                {
                    try
                    {
                        ProductoStockACero puesto = await repositorio.EnTransaccion(t => PonerUnoACero(t, empresa, almacen, traspaso, producto, usuario))
                            .ConfigureAwait(false);
                        if (puesto != null)
                        {
                            resultado.Puestos.Add(puesto);
                        }
                    }
                    catch (Exception ex)
                    {
                        ElmahHelper.Log(new Exception($"[Reposiciones] Sugerencia 753: no se ha podido poner a 0 el stock de {producto} en {almacen} " +
                            $"(reposición {traspaso}): {ServicioDiferenciasReposicion.MensajeDe(ex)}", ex));
                        fallidos.Add(producto);
                    }
                }
                if (resultado.Puestos.Any())
                {
                    resultado.Cambios.Add($"Stock de {nombre} a 0: " +
                        string.Join(", ", resultado.Puestos.Select(p => $"{p.Producto} (tenía {p.Tenia})")) + ".");
                }
                if (fallidos.Any())
                {
                    resultado.Aviso = $"No se ha podido poner a 0 el stock de {nombre} de {string.Join(", ", fallidos)}: " +
                        "la reposición está enviada; revise ese stock o avise a Informática.";
                    resultado.Cambios.Add(resultado.Aviso);
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Reposiciones] Sugerencia 753: no se ha podido poner a 0 el stock de lo que «No está» en {almacen} " +
                    $"(reposición {traspaso}): {ServicioDiferenciasReposicion.MensajeDe(ex)}", ex));
                resultado.Aviso = $"No se ha podido poner a 0 el stock de {string.Join(", ", lista)} en {almacen}: " +
                    "la reposición está enviada; revise ese stock o avise a Informática.";
                resultado.Cambios.Add(resultado.Aviso);
            }
            finally
            {
                (repositorio as IDisposable)?.Dispose();
            }
            return resultado;
        }

        private static async Task<ProductoStockACero> PonerUnoACero(ITransaccionStockACero t, string empresa, string almacen, int traspaso,
            string producto, string usuario)
        {
            int stock = await t.LeerStock(empresa, almacen, producto).ConfigureAwait(false);
            if (stock <= 0)
            {
                return null; // 0: ya está; negativo: no se inventan unidades (ver la cabecera)
            }
            ResultadoMovimientoStock movimiento = await t.Mover(new MovimientoStockDiferencia
            {
                Empresa = empresa,
                Almacen = almacen,
                Producto = producto,
                Cantidad = -stock,
                Traspaso = traspaso,
                Texto = Texto(traspaso, usuario),
                Usuario = usuario,
                PrimeroPendienteDeUbicar = false
            }).ConfigureAwait(false);
            return new ProductoStockACero { Producto = producto, Tenia = stock, NumeroOrdenExtracto = movimiento?.NumeroOrdenExtracto };
        }

        /// <summary>«Reposición 81028: «No está» al enviar (Paloma)», sin dominio y recortando el usuario para que quepa en 50.</summary>
        internal static string Texto(int traspaso, string usuario)
        {
            string limpio = usuario?.Trim() ?? string.Empty;
            int barra = limpio.LastIndexOf('\\');
            if (barra >= 0)
            {
                limpio = limpio.Substring(barra + 1);
            }
            string inicio = $"Reposición {traspaso}: «No está» al enviar (";
            int cabe = Math.Max(0, LONGITUD_TEXTO - inicio.Length - 1);
            if (limpio.Length > cabe)
            {
                limpio = limpio.Substring(0, cabe);
            }
            string texto = inicio + limpio + ")";
            return texto.Length > LONGITUD_TEXTO ? texto.Substring(0, LONGITUD_TEXTO) : texto;
        }
    }

    /// <summary>
    /// La transacción y el movimiento son los de #634 (<see cref="RepositorioDiferenciasReposicionSql.EnTransaccion{T}"/> y
    /// <see cref="TransaccionDiferenciasReposicionSql.Mover"/>): aquí solo se añaden las dos lecturas.
    /// </summary>
    public class RepositorioStockACeroSql : IRepositorioStockACero, IDisposable
    {
        internal const string SQL_ALMACEN = @"
SELECT RTRIM([Descripción]) AS Nombre, CAST(ControlUbicaciones AS bit) AS ControlUbicaciones
FROM Almacenes WHERE Empresa = @p0 AND [Número] = @p1";

        // El stock del almacén (índice extractoproducto11: Empresa, Número, Almacén, Cantidad), bloqueado hasta el apunte
        internal const string SQL_STOCK = @"
SELECT CAST(ISNULL(SUM(Cantidad), 0) AS int) FROM ExtractoProducto WITH (UPDLOCK, HOLDLOCK)
WHERE Empresa = @p0 AND [Número] = @p1 AND [Almacén] = @p2";

        private readonly NVEntities db;
        private readonly RepositorioDiferenciasReposicionSql diferencias;

        public RepositorioStockACeroSql(NVEntities db)
        {
            this.db = db;
            diferencias = new RepositorioDiferenciasReposicionSql(db);
        }

        public static IRepositorioStockACero ConContextoPropio()
        {
            return new RepositorioStockACeroSql(new NVEntities());
        }

        public Task<T> EnTransaccion<T>(Func<ITransaccionStockACero, Task<T>> trabajo)
        {
            return diferencias.EnTransaccion(t => trabajo(new TransaccionStockACeroSql(db, t)));
        }

        public void Dispose()
        {
            db?.Dispose();
        }

        private class TransaccionStockACeroSql : ITransaccionStockACero
        {
            private readonly NVEntities db;
            private readonly ITransaccionDiferenciasReposicion movimientos;

            public TransaccionStockACeroSql(NVEntities db, ITransaccionDiferenciasReposicion movimientos)
            {
                this.db = db;
                this.movimientos = movimientos;
            }

            public Task<AlmacenStockACero> LeerAlmacen(string empresa, string almacen)
            {
                return db.Database.SqlQuery<AlmacenStockACero>(SQL_ALMACEN,
                    DiferenciasReposicionSql.Char("@p0", empresa, 3), DiferenciasReposicionSql.Char("@p1", almacen, 3))
                    .FirstOrDefaultAsync();
            }

            public Task<int> LeerStock(string empresa, string almacen, string producto)
            {
                return db.Database.SqlQuery<int>(SQL_STOCK,
                    DiferenciasReposicionSql.Char("@p0", empresa, 3), DiferenciasReposicionSql.Char("@p1", producto, 15),
                    DiferenciasReposicionSql.Char("@p2", almacen, 3))
                    .SingleAsync();
            }

            public Task<ResultadoMovimientoStock> Mover(MovimientoStockDiferencia movimiento) => movimientos.Mover(movimiento);
        }
    }
}
