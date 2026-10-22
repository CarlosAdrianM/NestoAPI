using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.CorreosPostCompra
{
    /// <summary>NestoAPI#532: lo que ve la app de un tipo de correo (GET/PUT api/Clientes/PreferenciasCorreo).</summary>
    public class PreferenciaCorreoDTO
    {
        public string Tipo { get; set; }
        public string Nombre { get; set; }
        public string Descripcion { get; set; }
        /// <summary>True = lo recibe; false = se ha dado de baja (de este tipo o de todos).</summary>
        public bool Activo { get; set; }
    }

    /// <summary>NestoAPI#532: cuerpo del PUT api/Clientes/PreferenciasCorreo/{tipo}.</summary>
    public class PreferenciaCorreoRequest
    {
        public bool? Activo { get; set; }
    }

    /// <summary>NestoAPI#532: acceso a CorreosPostCompraBajas para un cliente (SQL a mano, fuera del EDMX, como el resto de #532).</summary>
    public interface IRepositorioPreferenciasCorreo
    {
        /// <summary>Las bajas de ese cliente o de esa dirección (de todo y por tipo). Sin la tabla, ninguna.</summary>
        Task<BajasCorreosPostCompra> LeerBajas(string empresa, string cliente, string email);
        /// <summary>Añade una baja del cliente. Tipo null = de todo.</summary>
        Task DarDeBaja(string empresa, string cliente, string tipo, string motivo, string usuario);
        /// <summary>Quita las bajas del cliente o de esa dirección con ese tipo (null = las de todo).</summary>
        Task QuitarBajas(string empresa, string cliente, string email, string tipo);
    }

    public class RepositorioPreferenciasCorreoSql : IRepositorioPreferenciasCorreo
    {
        internal const string SQL_LEER = @"
IF OBJECT_ID(N'dbo.CorreosPostCompraBajas', N'U') IS NULL
    SELECT CAST(NULL AS varchar(10)) AS Cliente, CAST(NULL AS varchar(255)) AS Email, CAST(NULL AS varchar(40)) AS Tipo WHERE 1 = 0
ELSE
    SELECT RTRIM(Cliente) AS Cliente, LTRIM(RTRIM(Email)) AS Email, RTRIM(Tipo) AS Tipo
    FROM dbo.CorreosPostCompraBajas WITH (NOLOCK)
    WHERE Empresa = @empresa AND (Cliente = @cliente OR (@email IS NOT NULL AND Email = @email))";

        internal const string SQL_DAR_DE_BAJA = @"
IF NOT EXISTS (SELECT 1 FROM dbo.CorreosPostCompraBajas WHERE Empresa = @empresa AND Cliente = @cliente
               AND ((@tipo IS NULL AND Tipo IS NULL) OR Tipo = @tipo))
    INSERT INTO dbo.CorreosPostCompraBajas (Empresa, Cliente, Email, Tipo, Motivo, Usuario)
    VALUES (@empresa, @cliente, NULL, @tipo, @motivo, @usuario)";

        internal const string SQL_QUITAR_BAJAS = @"
DELETE FROM dbo.CorreosPostCompraBajas
WHERE Empresa = @empresa AND (Cliente = @cliente OR (@email IS NOT NULL AND Email = @email))
  AND ((@tipo IS NULL AND Tipo IS NULL) OR Tipo = @tipo)";

        private readonly NVEntities db;

        public RepositorioPreferenciasCorreoSql(NVEntities db)
        {
            this.db = db;
        }

        public async Task<BajasCorreosPostCompra> LeerBajas(string empresa, string cliente, string email)
        {
            List<RepositorioRecordatorioReposicionSql.FilaBaja> filas = await db.Database
                .SqlQuery<RepositorioRecordatorioReposicionSql.FilaBaja>(SQL_LEER,
                    new SqlParameter("@empresa", empresa), new SqlParameter("@cliente", Texto(cliente)), new SqlParameter("@email", Texto(email)))
                .ToListAsync().ConfigureAwait(false);
            return RepositorioRecordatorioReposicionSql.ConstruirBajas(filas);
        }

        public async Task DarDeBaja(string empresa, string cliente, string tipo, string motivo, string usuario)
            => _ = await db.Database.ExecuteSqlCommandAsync(SQL_DAR_DE_BAJA,
                new SqlParameter("@empresa", empresa),
                new SqlParameter("@cliente", Texto(cliente)),
                new SqlParameter("@tipo", Texto(tipo)),
                new SqlParameter("@motivo", Texto(Recortar(motivo, 200))),
                new SqlParameter("@usuario", Texto(Recortar(usuario, 50)))).ConfigureAwait(false);

        public async Task QuitarBajas(string empresa, string cliente, string email, string tipo)
            => _ = await db.Database.ExecuteSqlCommandAsync(SQL_QUITAR_BAJAS,
                new SqlParameter("@empresa", empresa),
                new SqlParameter("@cliente", Texto(cliente)),
                new SqlParameter("@email", Texto(email)),
                new SqlParameter("@tipo", Texto(tipo))).ConfigureAwait(false);

        private static object Texto(string valor) => string.IsNullOrWhiteSpace(valor) ? (object)DBNull.Value : valor.Trim();

        private static string Recortar(string valor, int maximo)
            => valor == null || valor.Length <= maximo ? valor : valor.Substring(0, maximo);
    }

    /// <summary>
    /// NestoAPI#532 (bajas por tipo): qué correos recibe un cliente y cómo cambiarlo desde la app. Una baja de todo (Tipo NULL,
    /// las que se metían a mano) apaga todos los tipos; si el cliente enciende uno, la baja de todo se sustituye por bajas de
    /// cada uno de los demás tipos, para que solo cambie lo que ha tocado.
    /// </summary>
    public class ServicioPreferenciasCorreo
    {
        internal const string MOTIVO_APP = "Desde la app (preferencias de correo)";

        private readonly IRepositorioPreferenciasCorreo repositorio;

        public ServicioPreferenciasCorreo(IRepositorioPreferenciasCorreo repositorio)
        {
            this.repositorio = repositorio;
        }

        public async Task<List<PreferenciaCorreoDTO>> Leer(string empresa, string cliente, string email)
        {
            BajasCorreosPostCompra bajas = await repositorio.LeerBajas(empresa, cliente, email).ConfigureAwait(false)
                ?? new BajasCorreosPostCompra();
            return TiposCorreoCliente.Todos.Select(t => new PreferenciaCorreoDTO
            {
                Tipo = t.Tipo,
                Nombre = t.Nombre,
                Descripcion = t.Descripcion,
                Activo = !bajas.Incluye(cliente, email, t.Tipo)
            }).ToList();
        }

        /// <summary>Enciende o apaga un tipo. Null si el tipo no existe (no se toca nada).</summary>
        public async Task<List<PreferenciaCorreoDTO>> Cambiar(string empresa, string cliente, string email, string tipo, bool activo,
            string usuario)
        {
            TipoCorreoCliente tipoCorreo = TiposCorreoCliente.Buscar(tipo);
            if (tipoCorreo == null)
            {
                return null;
            }
            BajasCorreosPostCompra bajas = await repositorio.LeerBajas(empresa, cliente, email).ConfigureAwait(false)
                ?? new BajasCorreosPostCompra();

            if (!activo)
            {
                if (!bajas.Incluye(cliente, email, tipoCorreo.Tipo))
                {
                    await repositorio.DarDeBaja(empresa, cliente, tipoCorreo.Tipo, MOTIVO_APP, usuario).ConfigureAwait(false);
                }
            }
            else
            {
                if (bajas.Incluye(cliente, email))
                {
                    // Tenía la baja de todo: se queda de baja de los demás, cada uno por su lado
                    foreach (TipoCorreoCliente otro in TiposCorreoCliente.Todos.Where(t => t.Tipo != tipoCorreo.Tipo))
                    {
                        if (!bajas.IncluyeSoloTipo(cliente, email, otro.Tipo))
                        {
                            await repositorio.DarDeBaja(empresa, cliente, otro.Tipo, MOTIVO_APP, usuario).ConfigureAwait(false);
                        }
                    }
                    await repositorio.QuitarBajas(empresa, cliente, email, null).ConfigureAwait(false);
                }
                if (bajas.IncluyeSoloTipo(cliente, email, tipoCorreo.Tipo))
                {
                    await repositorio.QuitarBajas(empresa, cliente, email, tipoCorreo.Tipo).ConfigureAwait(false);
                }
            }
            return await Leer(empresa, cliente, email).ConfigureAwait(false);
        }

        /// <summary>
        /// Para los jobs que mandan un correo suelto (el posventa de los sábados): ¿está de baja de este tipo? Si no se puede
        /// leer (falta la columna Tipo porque no se ha lanzado el script), se loguea y se manda, como antes de existir las bajas.
        /// </summary>
        public static async Task<bool> EstaDeBaja(IRepositorioPreferenciasCorreo repositorio, string empresa, string cliente, string email,
            string tipo)
        {
            try
            {
                BajasCorreosPostCompra bajas = await repositorio.LeerBajas(empresa, cliente, email).ConfigureAwait(false);
                return bajas != null && bajas.Incluye(cliente, email, tipo);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Bajas de correo #532] No se han podido leer las bajas de {cliente} ({tipo}); se envía: {ex.Message}", ex));
                return false;
            }
        }
    }
}
