using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using NestoAPI.Models;
using NestoAPI.Models.Agencias;

namespace NestoAPI.Infraestructure.Agencias.RepartoPropio
{
    /// <summary>
    /// NestoAPI#786 (decisión de Carlos, 04/11/26): quiénes reparten. No hay grupo de seguridad de reparto (los repartidores están en
    /// Almacén, que también tiene a los del almacén), así que son los usuarios que han movido bultos de la furgoneta
    /// (RepartoBultosMovimientos con Origen «Repartidor») o los han cargado (EnviosAgenciaCargaBultos) en los últimos
    /// <see cref="ServicioRepartidores.DIAS"/> días.
    /// </summary>
    public interface IRepositorioRepartidores
    {
        /// <summary>Los usuarios (tal cual, con o sin dominio) con movimientos o cargas desde <paramref name="desde"/>. Sin las tablas, vacía.</summary>
        Task<List<string>> LeerUsuarios(DateTime desde);
    }

    public class RepositorioRepartidores : IRepositorioRepartidores
    {
        private readonly NVEntities db;

        public RepositorioRepartidores(NVEntities db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public async Task<List<string>> LeerUsuarios(DateTime desde)
        {
            var consultas = new List<string>();
            if (ColumnasOpcionales.ExisteTabla(db, RepositorioFurgoneta.TABLA))
            {
                consultas.Add($"SELECT RTRIM(Usuario) AS Usuario FROM dbo.{RepositorioFurgoneta.TABLA} WITH (NOLOCK) " +
                    $"WHERE FechaRegistro >= @desde AND Origen = '{OrigenesMovimientoBulto.REPARTIDOR}'");
            }
            if (ColumnasOpcionales.ExisteTabla(db, RepositorioCargaFurgoneta.TABLA))
            {
                consultas.Add($"SELECT RTRIM(Usuario) AS Usuario FROM dbo.{RepositorioCargaFurgoneta.TABLA} WITH (NOLOCK) WHERE FechaRegistro >= @desde");
            }
            if (consultas.Count == 0)
            {
                return new List<string>();
            }
            return await db.Database.SqlQuery<string>(string.Join(" UNION ", consultas),
                new SqlParameter("@desde", SqlDbType.DateTime) { Value = desde }).ToListAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// NestoAPI#786: la lista de repartidores (GET api/RepartoPropio/Repartidores) y la comprobación del repartidor de una nota de cobro.
    /// Sin dominio, sin repetidos (sin distinguir mayúsculas) y por nombre; siempre con el que lo pide (puede llevarla él mismo).
    /// </summary>
    public class ServicioRepartidores
    {
        public const int DIAS = 90;
        private static readonly CultureInfo Espanol = new CultureInfo("es-ES");

        private readonly IRepositorioRepartidores repositorio;
        private readonly Func<DateTime> ahora;

        public ServicioRepartidores(IRepositorioRepartidores repositorio, Func<DateTime> ahora = null)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
            this.ahora = ahora ?? (() => DateTime.Now);
        }

        public async Task<List<RepartidorDTO>> Lista(string usuarioQuePide)
        {
            List<string> usuarios = await repositorio.LeerUsuarios(ahora().Date.AddDays(-DIAS)).ConfigureAwait(false) ?? new List<string>();
            return Normalizar(usuarios.Concat(new[] { usuarioQuePide }));
        }

        /// <summary>El repartidor de la lista que es <paramref name="repartidor"/> (sin dominio ni mayúsculas), o null si no lo es.</summary>
        public async Task<RepartidorDTO> Buscar(string repartidor, string usuarioQuePide)
        {
            if (string.IsNullOrWhiteSpace(repartidor))
            {
                return null;
            }
            return (await Lista(usuarioQuePide).ConfigureAwait(false))
                .FirstOrDefault(r => ServicioRepartoPropio.MismoUsuario(r.Usuario, repartidor));
        }

        /// <summary>Sin dominio, sin vacíos ni repetidos, ordenados por nombre.</summary>
        internal static List<RepartidorDTO> Normalizar(IEnumerable<string> usuarios)
            => (usuarios ?? Enumerable.Empty<string>())
                .Select(ServicioRepartoPropio.NombreRepartidor)
                .Where(u => u != null)
                .GroupBy(u => u, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .Select(u => new RepartidorDTO { Usuario = u, Nombre = Nombre(u) })
                .OrderBy(r => r.Nombre, StringComparer.Create(Espanol, true))
                .ToList();

        /// <summary>«miguel» → «Miguel» (solo la primera letra; el resto, como venga).</summary>
        internal static string Nombre(string usuario)
            => string.IsNullOrEmpty(usuario) ? usuario : char.ToUpper(usuario[0], Espanol) + usuario.Substring(1);
    }
}
