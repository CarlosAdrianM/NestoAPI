using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using NestoAPI.Models;

namespace NestoAPI.Infraestructure.Agencias
{
    /// <summary>
    /// Incidencia 1328 (Novedades, 18/11/26): qué picking(s) lleva cada envío. EnviosAgencia solo guarda el número de pedido; un
    /// pedido servido en dos veces tiene dos envíos y no se sabía cuál llevaba qué (930358: el envío 250999, ENTREGADO, era la
    /// entrega parcial del picking 100095, y bloqueó aplazar las 6 líneas del picking 100114 que seguían en el almacén).
    /// <para>Las reglas puras: qué pickings van en un envío al crearlo (las líneas del pedido con picking aún sin facturar) y cuándo
    /// se apuntan al modificarlo. Unión, nunca se quita uno: un picking de más solo hace más prudente «¿la agencia ya lo tiene?».</para>
    /// </summary>
    public static class ReglasPickingsEnvio
    {
        /// <summary>
        /// Un envío que sabemos que no lleva ninguna línea del almacén (el retorno: una recogida en casa del cliente). Ningún picking
        /// de línea es 0, así que nunca «lleva» las líneas que cambian.
        /// </summary>
        public const int SIN_MERCANCIA = 0;

        /// <summary>
        /// Los pickings que van en el envío que se crea ahora: los de las líneas del pedido con picking en curso (1, lo que se está
        /// empaquetando) o en albarán (2, el albarán puede hacerse antes de la etiqueta). Los de un albarán anterior que ya salió en
        /// otro envío también entran: es de más, nunca de menos (sus líneas ya no cambian de fecha: <c>CambiaFechaEntregaDeLineaConPicking</c>
        /// solo mira las que no están en albarán). Sin ninguna, vacío: el envío queda SIN dato (cuenta como hoy, prudente).
        /// </summary>
        public static List<int> PickingsDeLasLineas(IEnumerable<LineaConPicking> lineas)
            => (lineas ?? Enumerable.Empty<LineaConPicking>())
                .Where(l => l != null && l.Picking.HasValue && l.Picking.Value > 0
                    && l.Estado >= Constantes.EstadosLineaVenta.EN_CURSO && l.Estado <= Constantes.EstadosLineaVenta.ALBARAN)
                .Select(l => l.Picking.Value)
                .Distinct().OrderBy(p => p).ToList();

        /// <summary>
        /// Al modificar un envío (PUT de Agencias de Nesto): se apuntan sus pickings si estaba aún en el almacén (pendiente o en curso:
        /// lo que lleva todavía puede crecer) y queda en curso o más (se registra o se tramita). Uno ya tramitado no se toca: lo que
        /// lleva ya está en la agencia, y apuntarle un picking nuevo del pedido sería decir que lleva lo que no lleva.
        /// </summary>
        public static bool ApuntarAlModificar(short? estadoAntes, short estadoDespues)
            => estadoAntes.HasValue && estadoAntes.Value < Constantes.Agencias.ESTADO_TRAMITADO
               && estadoDespues >= Constantes.Agencias.ESTADO_EN_CURSO;
    }

    /// <summary>Lo que se lee de una línea del pedido para saber su picking.</summary>
    public class LineaConPicking
    {
        public short Estado { get; set; }
        public int? Picking { get; set; }
    }

    /// <summary>Incidencia 1328: acceso a EnviosAgenciaPickings (fuera del EDMX). Nada de esto lanza nunca.</summary>
    public interface IRepositorioPickingsEnvio
    {
        bool TablaDisponible();
        /// <summary>Los pickings de cada envío CON dato; un envío sin filas no está (sin dato). Vacío si no hay tabla o falla.</summary>
        Task<Dictionary<int, List<int>>> LeerAsync(IEnumerable<int> envios);
        /// <summary>Apunta al envío los pickings de las líneas del pedido (<see cref="ReglasPickingsEnvio.PickingsDeLasLineas"/>).</summary>
        Task ApuntarDesdeLineasAsync(int envio, int? pedido, string usuario);
        /// <summary>Los pickings que se sabe que lleva (sin repetir los que ya tenía).</summary>
        Task ApuntarAsync(int envio, IEnumerable<int> pickings, string usuario);
        /// <summary>El reenvío lleva lo mismo que el original: le copia sus filas (si el original no tiene, el nuevo tampoco).</summary>
        Task CopiarAsync(int envioOrigen, int envioNuevo, string usuario);
    }

    /// <summary>
    /// Incidencia 1328: EnviosAgenciaPickings por SQL, comprobando que la tabla existe (<see cref="ColumnasOpcionales.ExisteTabla"/>).
    /// Sin el script, ni lee ni escribe (todo como antes: los envíos, sin dato) y lo dice en ELMAH una vez. Si algo falla, ELMAH y
    /// sigue: apuntar o leer los pickings nunca rompe crear un envío ni guardar un pedido.
    /// </summary>
    public class RepositorioPickingsEnvio : IRepositorioPickingsEnvio
    {
        public const string TABLA = "EnviosAgenciaPickings";

        private static int avisadoSinTabla;
        private readonly NVEntities db;
        private readonly Action<Exception> registrar;

        public RepositorioPickingsEnvio(NVEntities db, Action<Exception> registrar = null)
        {
            this.db = db;
            this.registrar = registrar ?? (ex => ElmahHelper.Log(ex));
        }

        public bool TablaDisponible()
        {
            bool existe = db != null && ColumnasOpcionales.ExisteTabla(db, TABLA);
            if (!existe && System.Threading.Interlocked.Exchange(ref avisadoSinTabla, 1) == 0)
            {
                Registrar(new Exception($"[Pickings del envío, incidencia 1328] No está la tabla dbo.{TABLA} (falta lanzar " +
                    "Scripts/Issue1328_EnviosAgenciaPickings.sql): los envíos no saben qué picking llevan, todo como antes."));
            }
            return existe;
        }

        public async Task<Dictionary<int, List<int>>> LeerAsync(IEnumerable<int> envios)
        {
            var resultado = new Dictionary<int, List<int>>();
            try
            {
                List<int> numeros = (envios ?? Enumerable.Empty<int>()).Where(n => n > 0).Distinct().ToList();
                if (numeros.Count == 0 || !TablaDisponible())
                {
                    return resultado;
                }
                // Enteros: van en el IN sin riesgo de inyección. Pocos (los envíos de un pedido).
                string lista = string.Join(",", numeros.Select(n => n.ToString(CultureInfo.InvariantCulture)));
                List<FilaPickingEnvio> filas = await db.Database.SqlQuery<FilaPickingEnvio>(
                    $"SELECT Envio, Picking FROM dbo.{TABLA} WHERE Envio IN ({lista})").ToListAsync().ConfigureAwait(true);
                foreach (IGrouping<int, FilaPickingEnvio> grupo in filas.GroupBy(f => f.Envio))
                {
                    resultado[grupo.Key] = grupo.Select(f => f.Picking).Distinct().OrderBy(p => p).ToList();
                }
            }
            catch (Exception ex)
            {
                // Sin dato: «¿la agencia ya lo tiene?» contesta como antes (prudente).
                Registrar(new Exception($"[Pickings del envío, incidencia 1328] No se han podido leer los pickings de los envíos: {ex.Message}", ex));
                resultado.Clear();
            }
            return resultado;
        }

        public async Task ApuntarDesdeLineasAsync(int envio, int? pedido, string usuario)
        {
            if (envio <= 0 || !pedido.HasValue || pedido.Value <= 0)
            {
                return;
            }
            try
            {
                if (!TablaDisponible())
                {
                    return;
                }
                int numero = pedido.Value;
                // Solo por número (la empresa del envío puede ser la de la agencia, la espejo): de más, nunca de menos.
                List<LineaConPicking> lineas = await db.LinPedidoVtas.AsNoTracking()
                    .Where(l => l.Número == numero && l.Picking > 0)
                    .Select(l => new LineaConPicking { Estado = l.Estado, Picking = l.Picking })
                    .ToListAsync().ConfigureAwait(true);
                await Insertar(envio, ReglasPickingsEnvio.PickingsDeLasLineas(lineas), usuario).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                Registrar(new Exception($"[Pickings del envío, incidencia 1328] Envío {envio} (pedido {pedido}): no se han podido apuntar sus pickings: {ex.Message}", ex));
            }
        }

        public async Task ApuntarAsync(int envio, IEnumerable<int> pickings, string usuario)
        {
            if (envio <= 0)
            {
                return;
            }
            try
            {
                if (!TablaDisponible())
                {
                    return;
                }
                await Insertar(envio, (pickings ?? Enumerable.Empty<int>()).Where(p => p >= 0).Distinct().ToList(), usuario).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                Registrar(new Exception($"[Pickings del envío, incidencia 1328] Envío {envio}: no se han podido apuntar sus pickings: {ex.Message}", ex));
            }
        }

        public async Task CopiarAsync(int envioOrigen, int envioNuevo, string usuario)
        {
            if (envioOrigen <= 0 || envioNuevo <= 0)
            {
                return;
            }
            Dictionary<int, List<int>> origen = await LeerAsync(new[] { envioOrigen }).ConfigureAwait(true);
            if (origen.TryGetValue(envioOrigen, out List<int> pickings) && pickings.Count > 0)
            {
                await ApuntarAsync(envioNuevo, pickings, usuario).ConfigureAwait(true);
            }
        }

        private async Task Insertar(int envio, List<int> pickings, string usuario)
        {
            if (pickings == null || pickings.Count == 0)
            {
                return;
            }
            string quien = string.IsNullOrWhiteSpace(usuario) ? "NestoAPI" : usuario.Trim();
            quien = quien.Length <= 50 ? quien : quien.Substring(0, 50);
            foreach (int picking in pickings)
            {
                _ = await db.Database.ExecuteSqlCommandAsync(
                    $"IF NOT EXISTS (SELECT 1 FROM dbo.{TABLA} WHERE Envio = @p0 AND Picking = @p1) " +
                    $"INSERT INTO dbo.{TABLA} (Envio, Picking, FechaAlta, Usuario) VALUES (@p0, @p1, GETDATE(), @p2)",
                    envio, picking, quien).ConfigureAwait(true);
            }
        }

        private void Registrar(Exception ex)
        {
            try
            {
                registrar(ex);
            }
            catch
            {
                // El registro nunca rompe la operación.
            }
        }

        /// <summary>Una fila de EnviosAgenciaPickings (pública: la materializa SqlQuery).</summary>
        public class FilaPickingEnvio
        {
            public int Envio { get; set; }
            public int Picking { get; set; }
        }
    }
}
