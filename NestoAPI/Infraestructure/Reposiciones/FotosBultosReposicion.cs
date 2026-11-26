using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Infrastructure;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Net;
using System.Security.Principal;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Reposiciones
{
    /// <summary>
    /// NestoAPI#906 (Carlos 24 y 26/11/26: «evitará disputas»): una foto por bulto R{traspaso}-{n} al ENVIAR la reposición, con su
    /// contenido a la vista antes de cerrarlo, como las fotos de bulto del packing (mismo blob «bultos», mismas reglas de JPEG y tope de 4 MB
    /// que la foto del mozo, <see cref="FotoMozoAvisoFicha"/>). Se guarda en <c>dbo.ReposicionesBultosFotos</c> (una fila por bulto: la última
    /// sustituye a la anterior; las anteriores siguen en el blob). Una foto de antes de rehacer los bultos (FechaFoto anterior a la creación
    /// del bulto) no cuenta. HOY la regla de «quién pierde» (#906) no la usa. Sin la tabla o sin el blob, 503 y todo como hasta ahora.
    /// </summary>
    public class FotoBultoReposicionDTO
    {
        public string Bulto { get; set; }
        public string Ruta { get; set; }
        public DateTime Fecha { get; set; }
        public string Usuario { get; set; }
    }

    /// <summary>POST api/Reposiciones/{traspaso}/Bultos/{bulto}/Foto.</summary>
    public class SubirFotoBultoReposicionDTO
    {
        /// <summary>JPEG en base64 (con o sin «data:image/jpeg;base64,»), 4 MB como mucho.</summary>
        public string FotoBase64 { get; set; }
    }

    /// <summary>Qué bultos de una reposición tienen foto (seguimiento y discusión de diferencias).</summary>
    public class BultoConFotoReposicionDTO
    {
        public string Codigo { get; set; }
        public int Bulto { get; set; }
        public bool TieneFoto { get; set; }
    }

    /// <summary>Un bulto de la reposición con su foto vigente (si la tiene).</summary>
    public class FilaFotoBultoReposicion
    {
        public int Traspaso { get; set; }
        public short Bulto { get; set; }
        public string Codigo { get; set; }
        public string RutaBlob { get; set; }
        public DateTime? FechaFoto { get; set; }
        public string UsuarioFoto { get; set; }
    }

    public interface IRepositorioFotosBultosReposicion
    {
        bool TablaCreada();
        Task<OrigenDestinoReposicion> LeerOrigenDestino(string empresa, int traspaso);
        /// <summary>Los bultos de esos traspasos, cada uno con su foto vigente (o sin ella).</summary>
        Task<List<FilaFotoBultoReposicion>> Leer(string empresa, IReadOnlyCollection<int> traspasos);
        Task Guardar(string empresa, int traspaso, short bulto, string ruta, DateTime fecha, string usuario, int bytes);
    }

    /// <summary>LAS REGLAS (puras) de las fotos de bulto de una reposición.</summary>
    public static class ReglasFotosBultosReposicion
    {
        public const string TABLA = "ReposicionesBultosFotos";
        public const string MENSAJE_SIN_TABLA =
            "Las fotos de los bultos de las reposiciones aún no están disponibles (falta el script Issue906_ReposicionesBultosFotos.sql).";
        public const string MENSAJE_SIN_BLOB = "Las fotos de los bultos no se pueden guardar: el almacenamiento de fotos no está configurado.";
        public const string MENSAJE_SIN_FOTO = "Falta la foto del bulto.";

        /// <summary>{empresa}/reposiciones/{traspaso}/bulto-{n}-{guid}.jpg: repetir la foto no pisa la anterior en el blob.</summary>
        public static string Ruta(string empresa, int traspaso, int bulto, Guid id)
            => $"reposiciones/{empresa?.Trim()}/{traspaso}/bulto-{bulto}-{id:N}.jpg";

        /// <summary>Quién VE la foto: quien prepara la reposición (origen), la gente del destino, Almacén, Dirección e Informática.</summary>
        public static bool PuedeVer(IPrincipal usuario, bool puedeOrigen, bool puedeDestino)
            => puedeOrigen || puedeDestino
               || (usuario != null && (usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.DIRECCION)
                                       || usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.INFORMATICA)));

        public static List<BultoConFotoReposicionDTO> Bultos(IEnumerable<FilaFotoBultoReposicion> filas, int traspaso)
            => (filas ?? Enumerable.Empty<FilaFotoBultoReposicion>())
                .Where(f => f.Traspaso == traspaso)
                .OrderBy(f => f.Bulto)
                .Select(f => new BultoConFotoReposicionDTO
                {
                    Codigo = f.Codigo?.Trim(),
                    Bulto = f.Bulto,
                    TieneFoto = !string.IsNullOrWhiteSpace(f.RutaBlob)
                })
                .ToList();
    }

    public class ServicioFotosBultosReposicion
    {
        public static readonly TimeSpan VIGENCIA_ENLACE = TimeSpan.FromMinutes(15);

        private readonly IRepositorioFotosBultosReposicion repositorio;
        private readonly IAlmacenFotosBultos fotos;
        private readonly Func<IPrincipal, string, string, bool> puedeEscribir;
        private readonly Func<DateTime> ahora;

        /// <param name="puedeEscribir">Quien prepara la reposición de un origen (<see cref="IServicioPreparacionReposicion.PuedeEscribir"/>).</param>
        public ServicioFotosBultosReposicion(IRepositorioFotosBultosReposicion repositorio, IAlmacenFotosBultos fotos,
            Func<IPrincipal, string, string, bool> puedeEscribir, Func<DateTime> ahora = null)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
            this.fotos = fotos;
            this.puedeEscribir = puedeEscribir ?? throw new ArgumentNullException(nameof(puedeEscribir));
            this.ahora = ahora ?? (() => DateTime.Now);
        }

        public async Task<FotoBultoReposicionDTO> Subir(string empresa, int traspaso, int bulto, SubirFotoBultoReposicionDTO peticion, IPrincipal usuario)
        {
            if (string.IsNullOrWhiteSpace(peticion?.FotoBase64))
            {
                throw new NestoBusinessException(ReglasFotosBultosReposicion.MENSAJE_SIN_FOTO);
            }
            string motivoFoto = FotoMozoAvisoFicha.Leer(peticion.FotoBase64, out byte[] bytes);
            if (motivoFoto != null)
            {
                throw new NestoBusinessException(motivoFoto);
            }
            ExigirTabla();
            if (fotos == null || !fotos.Configurado)
            {
                throw new NestoBusinessException(ReglasFotosBultosReposicion.MENSAJE_SIN_BLOB) { StatusCode = HttpStatusCode.ServiceUnavailable };
            }
            OrigenDestinoReposicion od = await repositorio.LeerOrigenDestino(empresa, traspaso).ConfigureAwait(false);
            // Sin saber el origen, solo Almacén y Dirección (PuedeEscribir los deja con cualquier origen)
            if (!puedeEscribir(usuario, empresa, od?.Origen?.Trim() ?? string.Empty))
            {
                throw new NestoBusinessException($"La foto de los bultos de la reposición {traspaso} la hace quien la prepara (o Almacén y Dirección).")
                { StatusCode = HttpStatusCode.Forbidden };
            }
            FilaFotoBultoReposicion fila = await LeerBulto(empresa, traspaso, bulto).ConfigureAwait(false);
            string usuarioAuditoria = UsuarioAuditoriaHelper.ParaAuditoria(UsuarioAuditoriaHelper.Resolver(usuario, null));
            DateTime fecha = ahora();
            string ruta = ReglasFotosBultosReposicion.Ruta(empresa, traspaso, fila.Bulto, Guid.NewGuid());
            await fotos.Subir(ruta, bytes, "image/jpeg").ConfigureAwait(false);
            await repositorio.Guardar(empresa, traspaso, fila.Bulto, ruta, fecha, usuarioAuditoria, bytes.Length).ConfigureAwait(false);
            return new FotoBultoReposicionDTO { Bulto = fila.Codigo?.Trim(), Ruta = ruta, Fecha = fecha, Usuario = usuarioAuditoria };
        }

        /// <summary>El enlace (de 15 minutos) para ver la foto del bulto. 404 sin bulto o sin foto; 403; 503 sin tabla o sin blob.</summary>
        public async Task<Uri> Enlace(string empresa, int traspaso, int bulto, IPrincipal usuario)
        {
            ExigirTabla();
            OrigenDestinoReposicion od = await repositorio.LeerOrigenDestino(empresa, traspaso).ConfigureAwait(false);
            bool puedeOrigen = !string.IsNullOrWhiteSpace(od?.Origen) && puedeEscribir(usuario, empresa, od.Origen.Trim());
            bool puedeDestino = !string.IsNullOrWhiteSpace(od?.Destino) && puedeEscribir(usuario, empresa, od.Destino.Trim());
            if (!ReglasFotosBultosReposicion.PuedeVer(usuario, puedeOrigen, puedeDestino))
            {
                throw new NestoBusinessException($"Las fotos de los bultos de la reposición {traspaso} las ven el origen, el destino, Almacén y Dirección.")
                { StatusCode = HttpStatusCode.Forbidden };
            }
            FilaFotoBultoReposicion fila = await LeerBulto(empresa, traspaso, bulto).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(fila.RutaBlob))
            {
                throw new NestoBusinessException($"El bulto {fila.Codigo?.Trim()} no tiene foto.") { StatusCode = HttpStatusCode.NotFound };
            }
            if (fotos == null || !fotos.Configurado)
            {
                throw new NestoBusinessException(ReglasFotosBultosReposicion.MENSAJE_SIN_BLOB) { StatusCode = HttpStatusCode.ServiceUnavailable };
            }
            return fotos.EnlaceDeLectura(fila.RutaBlob.Trim(), VIGENCIA_ENLACE);
        }

        /// <summary>Qué bultos tienen foto, por traspaso (vacío sin la tabla o si falla la lectura: nunca lanza).</summary>
        public async Task<Dictionary<int, List<BultoConFotoReposicionDTO>>> ConFoto(string empresa, IEnumerable<int> traspasos)
        {
            var resultado = new Dictionary<int, List<BultoConFotoReposicionDTO>>();
            List<int> lista = (traspasos ?? Enumerable.Empty<int>()).Where(t => t > 0).Distinct().ToList();
            if (!lista.Any())
            {
                return resultado;
            }
            try
            {
                if (!repositorio.TablaCreada())
                {
                    return resultado;
                }
                List<FilaFotoBultoReposicion> filas = await repositorio.Leer(empresa, lista).ConfigureAwait(false) ?? new List<FilaFotoBultoReposicion>();
                foreach (int traspaso in lista)
                {
                    resultado[traspaso] = ReglasFotosBultosReposicion.Bultos(filas, traspaso);
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Fotos de bultos de reposición #906] No se han podido leer: {ex.Message}", ex));
            }
            return resultado;
        }

        /// <summary>Pone TieneFoto en los bultos del seguimiento. Nunca lanza.</summary>
        public async Task Completar(string empresa, SeguimientoReposicionDTO seguimiento)
        {
            if (seguimiento?.Bultos == null || !seguimiento.Bultos.Any())
            {
                return;
            }
            Dictionary<int, List<BultoConFotoReposicionDTO>> conFoto = await ConFoto(empresa, new[] { seguimiento.Traspaso }).ConfigureAwait(false);
            if (!conFoto.TryGetValue(seguimiento.Traspaso, out List<BultoConFotoReposicionDTO> bultos))
            {
                return;
            }
            foreach (BultoSeguimientoReposicionDTO bulto in seguimiento.Bultos)
            {
                bulto.TieneFoto = bultos.Any(b => b.Bulto == bulto.Bulto && b.TieneFoto);
            }
        }

        /// <summary>Pone los bultos (con TieneFoto) en cada discusión, con una lectura para todos sus traspasos. Nunca lanza.</summary>
        public async Task Completar(string empresa, IEnumerable<DiscusionDiferenciaDTO> discusiones)
        {
            List<DiscusionDiferenciaDTO> lista = (discusiones ?? Enumerable.Empty<DiscusionDiferenciaDTO>()).Where(d => d != null).ToList();
            if (!lista.Any())
            {
                return;
            }
            Dictionary<int, List<BultoConFotoReposicionDTO>> conFoto = await ConFoto(empresa, lista.Select(d => d.Traspaso)).ConfigureAwait(false);
            foreach (DiscusionDiferenciaDTO discusion in lista)
            {
                if (conFoto.TryGetValue(discusion.Traspaso, out List<BultoConFotoReposicionDTO> bultos))
                {
                    discusion.Bultos = bultos;
                }
            }
        }

        private void ExigirTabla()
        {
            if (!repositorio.TablaCreada())
            {
                throw new NestoBusinessException(ReglasFotosBultosReposicion.MENSAJE_SIN_TABLA) { StatusCode = HttpStatusCode.ServiceUnavailable };
            }
        }

        private async Task<FilaFotoBultoReposicion> LeerBulto(string empresa, int traspaso, int bulto)
        {
            List<FilaFotoBultoReposicion> filas = await repositorio.Leer(empresa, new[] { traspaso }).ConfigureAwait(false) ?? new List<FilaFotoBultoReposicion>();
            FilaFotoBultoReposicion fila = filas.FirstOrDefault(f => f.Traspaso == traspaso && f.Bulto == bulto);
            if (fila == null)
            {
                throw new NestoBusinessException($"La reposición {traspaso} no tiene el bulto {bulto}.") { StatusCode = HttpStatusCode.NotFound };
            }
            return fila;
        }
    }

    public class RepositorioFotosBultosReposicionSql : IRepositorioFotosBultosReposicion
    {
        private readonly NVEntities db;

        public RepositorioFotosBultosReposicionSql(NVEntities db)
        {
            this.db = db; // null (tests del controlador sin base de datos) = sin la tabla
        }

        /// <summary>La foto vale si es de después de crear el bulto (rehacer los bultos deja las fotos viejas fuera).</summary>
        internal const string SQL_LEER = @"
SELECT b.NumTraspaso AS Traspaso, b.Bulto, RTRIM(b.Codigo) AS Codigo,
       CASE WHEN f.Fecha >= b.FechaCreacion THEN RTRIM(f.RutaBlob) END AS RutaBlob,
       CASE WHEN f.Fecha >= b.FechaCreacion THEN f.Fecha END AS FechaFoto,
       CASE WHEN f.Fecha >= b.FechaCreacion THEN RTRIM(f.Usuario) END AS UsuarioFoto
FROM dbo.ReposicionesBultos b
LEFT JOIN dbo.ReposicionesBultosFotos f ON f.Empresa = b.Empresa AND f.NumTraspaso = b.NumTraspaso AND f.Bulto = b.Bulto
WHERE b.Empresa = @p0 AND b.NumTraspaso IN ({0})
ORDER BY b.NumTraspaso, b.Bulto";

        internal const string SQL_GUARDAR = @"
UPDATE dbo.ReposicionesBultosFotos SET RutaBlob = @ruta, Fecha = @fecha, Usuario = @usuario, Bytes = @bytes
WHERE Empresa = @empresa AND NumTraspaso = @traspaso AND Bulto = @bulto;
IF @@ROWCOUNT = 0
    INSERT INTO dbo.ReposicionesBultosFotos (Empresa, NumTraspaso, Bulto, RutaBlob, Fecha, Usuario, Bytes)
    VALUES (@empresa, @traspaso, @bulto, @ruta, @fecha, @usuario, @bytes);";

        public bool TablaCreada()
            => db != null && ColumnasOpcionales.ExisteTabla(db, ReglasFotosBultosReposicion.TABLA) && ColumnasOpcionales.ExisteTabla(db, "ReposicionesBultos");

        public Task<OrigenDestinoReposicion> LeerOrigenDestino(string empresa, int traspaso)
            => ReposicionesBultosSql.LeerOrigenDestino(db.Database, empresa, traspaso);

        public async Task<List<FilaFotoBultoReposicion>> Leer(string empresa, IReadOnlyCollection<int> traspasos)
        {
            List<int> lista = (traspasos ?? new List<int>()).Distinct().Take(500).ToList();
            if (!lista.Any())
            {
                return new List<FilaFotoBultoReposicion>();
            }
            var parametros = new List<object> { new SqlParameter("@p0", SqlDbType.Char, 3) { Value = empresa?.Trim() ?? Constantes.Empresas.EMPRESA_POR_DEFECTO } };
            var nombres = new List<string>();
            for (int i = 0; i < lista.Count; i++)
            {
                nombres.Add("@t" + i);
                parametros.Add(new SqlParameter("@t" + i, SqlDbType.Int) { Value = lista[i] });
            }
            return await db.Database.SqlQuery<FilaFotoBultoReposicion>(string.Format(SQL_LEER, string.Join(", ", nombres)), parametros.ToArray())
                .ToListAsync().ConfigureAwait(false);
        }

        public Task Guardar(string empresa, int traspaso, short bulto, string ruta, DateTime fecha, string usuario, int bytes)
        {
            return db.Database.ExecuteSqlCommandAsync(SQL_GUARDAR,
                new SqlParameter("@empresa", SqlDbType.Char, 3) { Value = empresa?.Trim() ?? Constantes.Empresas.EMPRESA_POR_DEFECTO },
                new SqlParameter("@traspaso", SqlDbType.Int) { Value = traspaso },
                new SqlParameter("@bulto", SqlDbType.SmallInt) { Value = bulto },
                new SqlParameter("@ruta", SqlDbType.VarChar, 300) { Value = ruta },
                new SqlParameter("@fecha", SqlDbType.DateTime) { Value = fecha },
                new SqlParameter("@usuario", SqlDbType.VarChar, 50) { Value = (object)usuario ?? DBNull.Value },
                new SqlParameter("@bytes", SqlDbType.Int) { Value = bytes });
        }
    }
}
