using NestoAPI.Models;
using System;
using System.Collections.Concurrent;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.ParametrosUsuario
{
    /// <summary>Qué hace PUT api/ParametrosUsuario con la fila pedida.</summary>
    public enum AccionGuardarParametroUsuario
    {
        /// <summary>La fila del usuario existe: se cambia.</summary>
        Modificar,
        /// <summary>No existe la del usuario pero sí la «(defecto)» de esa clave: se crea la del usuario con el valor pedido.</summary>
        CrearDesdeDefecto,
        /// <summary>No existe ni la del usuario ni la «(defecto)»: el parámetro no está dado de alta (404).</summary>
        NoDadoDeAlta
    }

    /// <summary>
    /// Incidencia 1481 (Lidia 26/11/26): la casilla de los borradores de Nesto daba «No se ha podido guardar su preferencia» porque
    /// PUT api/ParametrosUsuario devolvía 404 cuando el usuario aún no tenía su fila (el PUT solo modificaba). LA REGLA (pura): con la
    /// «(defecto)» de la clave, la fila del usuario se crea con el valor pedido (como hace el GET al leerla); sin ella, 404 de usted.
    /// Los permisos los decide antes <see cref="ReglaCambioParametroUsuario"/>.
    /// </summary>
    public static class ReglaGuardarParametroUsuario
    {
        internal const string MOTIVO_NO_DADO_DE_ALTA = "El parámetro {0} no está dado de alta: avise a Informática.";

        public static AccionGuardarParametroUsuario Decidir(bool existeFilaUsuario, bool existeFilaDefecto)
        {
            if (existeFilaUsuario)
            {
                return AccionGuardarParametroUsuario.Modificar;
            }
            return existeFilaDefecto ? AccionGuardarParametroUsuario.CrearDesdeDefecto : AccionGuardarParametroUsuario.NoDadoDeAlta;
        }

        public static string MotivoNoDadoDeAlta(string clave) => string.Format(MOTIVO_NO_DADO_DE_ALTA, (clave ?? string.Empty).Trim());

        /// <summary>La fila nueva del usuario: el valor pedido, con quién y cuándo.</summary>
        public static ParametroUsuario FilaNueva(ParametroUsuario pedida, DateTime ahora)
        {
            return new ParametroUsuario
            {
                Empresa = pedida.Empresa?.Trim(),
                Usuario = pedida.Usuario?.Trim(),
                Clave = pedida.Clave?.Trim(),
                Valor = pedida.Valor,
                Usuario2 = string.IsNullOrWhiteSpace(pedida.Usuario2) ? pedida.Usuario?.Trim() : pedida.Usuario2.Trim(),
                Fecha_Modificación = pedida.Fecha_Modificación == default(DateTime) ? ahora : pedida.Fecha_Modificación
            };
        }
    }

    /// <summary>
    /// Incidencia 1481: una línea en ELMAH por clave y día (en memoria) cuando un PUT pide un parámetro que no está dado de alta,
    /// para enterarnos sin llenar el registro.
    /// </summary>
    public static class AvisoParametroNoDadoDeAlta
    {
        private static readonly ConcurrentDictionary<string, DateTime> avisados = new ConcurrentDictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        /// <summary>¿Toca avisar hoy de esta clave? (la primera vez del día, sí; después, no).</summary>
        public static bool TocaAvisar(string empresa, string clave, DateTime hoy)
        {
            string llave = $"{empresa?.Trim()}|{clave?.Trim()}";
            DateTime dia = hoy.Date;
            bool toca = false;
            _ = avisados.AddOrUpdate(llave,
                _ => { toca = true; return dia; },
                (_, anterior) =>
                {
                    if (anterior < dia)
                    {
                        toca = true;
                        return dia;
                    }
                    return anterior;
                });
            return toca;
        }

        public static void Avisar(string empresa, string clave, string usuario, DateTime hoy)
        {
            if (!TocaAvisar(empresa, clave, hoy))
            {
                return;
            }
            ElmahHelper.Log(new Exception($"[ParametrosUsuario] PUT de un parámetro que no está dado de alta (sin fila «(defecto)»): " +
                $"empresa {empresa?.Trim()}, clave «{clave?.Trim()}», usuario {usuario?.Trim()}. Dé de alta la fila «(defecto)» si es un parámetro nuevo."));
        }

        internal static void Reiniciar() => avisados.Clear();
    }

    /// <summary>Las lecturas y escrituras del PUT (sustituible en los tests).</summary>
    public interface IAlmacenParametrosUsuario
    {
        Task<bool> Existe(string empresa, string usuario, string clave);
        Task Modificar(ParametroUsuario parametro);
        Task Crear(ParametroUsuario parametro);
    }

    public class AlmacenParametrosUsuarioEF : IAlmacenParametrosUsuario
    {
        private readonly NVEntities db;

        public AlmacenParametrosUsuarioEF(NVEntities db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        // Where + Any: el filtro va en SQL, que ignora el relleno de los char.
        public Task<bool> Existe(string empresa, string usuario, string clave)
            => db.ParametrosUsuario.AnyAsync(p => p.Empresa == empresa && p.Usuario == usuario && p.Clave == clave);

        public async Task Modificar(ParametroUsuario parametro)
        {
            db.Entry(parametro).State = EntityState.Modified;
            _ = await db.SaveChangesAsync().ConfigureAwait(false);
        }

        public async Task Crear(ParametroUsuario parametro)
        {
            _ = db.ParametrosUsuario.Add(parametro);
            _ = await db.SaveChangesAsync().ConfigureAwait(false);
        }
    }

    /// <summary>El núcleo del PUT ya con los permisos comprobados: modificar, crear desde «(defecto)» o decir que no está dado de alta.</summary>
    public class ServicioGuardarParametroUsuario
    {
        private readonly IAlmacenParametrosUsuario almacen;
        private readonly Func<DateTime> ahora;

        public ServicioGuardarParametroUsuario(IAlmacenParametrosUsuario almacen, Func<DateTime> ahora = null)
        {
            this.almacen = almacen ?? throw new ArgumentNullException(nameof(almacen));
            this.ahora = ahora ?? (() => DateTime.Now);
        }

        public async Task<AccionGuardarParametroUsuario> Guardar(ParametroUsuario parametro)
        {
            string empresa = parametro.Empresa?.Trim();
            string usuario = parametro.Usuario?.Trim();
            string clave = parametro.Clave?.Trim();
            bool existeUsuario = await almacen.Existe(empresa, usuario, clave).ConfigureAwait(false);
            bool existeDefecto = !existeUsuario
                && await almacen.Existe(empresa, ReglaCambioParametroUsuario.USUARIO_DEFECTO, clave).ConfigureAwait(false);
            AccionGuardarParametroUsuario accion = ReglaGuardarParametroUsuario.Decidir(existeUsuario, existeDefecto);
            switch (accion)
            {
                case AccionGuardarParametroUsuario.Modificar:
                    try
                    {
                        await almacen.Modificar(parametro).ConfigureAwait(false);
                    }
                    catch (DbUpdateConcurrencyException)
                    {
                        // Se ha borrado mientras: se vuelve a decidir una vez.
                        if (await almacen.Existe(empresa, usuario, clave).ConfigureAwait(false))
                        {
                            throw;
                        }
                        if (!await almacen.Existe(empresa, ReglaCambioParametroUsuario.USUARIO_DEFECTO, clave).ConfigureAwait(false))
                        {
                            AvisoParametroNoDadoDeAlta.Avisar(empresa, clave, usuario, ahora());
                            return AccionGuardarParametroUsuario.NoDadoDeAlta;
                        }
                        await almacen.Crear(ReglaGuardarParametroUsuario.FilaNueva(parametro, ahora())).ConfigureAwait(false);
                        return AccionGuardarParametroUsuario.CrearDesdeDefecto;
                    }
                    break;
                case AccionGuardarParametroUsuario.CrearDesdeDefecto:
                    await almacen.Crear(ReglaGuardarParametroUsuario.FilaNueva(parametro, ahora())).ConfigureAwait(false);
                    break;
                default:
                    AvisoParametroNoDadoDeAlta.Avisar(empresa, clave, usuario, ahora());
                    break;
            }
            return accion;
        }
    }
}
