using NestoAPI.Infrastructure;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Security.Principal;
using System.Text;

namespace NestoAPI.Infraestructure.Novedades
{
    /// <summary>
    /// Sugerencia 551 (Alberto Sancho, 09/10/26): «que solo salgan las novedades que le afecten a cada uno».
    /// Cada novedad dice a quién afecta (columna Novedades.Perfiles, Scripts/Sugerencia551_NovedadesPerfiles.sql)
    /// y cada usuario ve por defecto las de sus perfiles, que se deducen de lo que ya trae su token:
    ///
    /// <code>
    ///   Grupo de dominio / rol / claim                 Perfil
    ///   ---------------------------------------------  ----------------------------------
    ///   Dirección, Informatica (o Informática)         ve TODAS (redacta y revisa)
    ///   Almacén                                        Almacén
    ///   Tiendas                                        Tiendas
    ///   Administración, Compras, TiendaOnline          Administración
    ///   Comerciales o claim IsVendedor (NestoApp)      Vendedores, SOLO si no tiene ninguno de los
    ///                                                  tres de arriba: Comerciales lo tienen también
    ///                                                  almacén y tiendas (permiso de ver clientes)
    ///   ninguno de los anteriores, o sin token         ve TODAS (no sabemos a quién afecta)
    /// </code>
    ///
    /// Una novedad sin perfiles (NULL, vacía o «Todos») es para todos: así las de antes siguen viéndose igual.
    /// Las sugerencias e incidencias de los usuarios no se filtran nunca.
    /// </summary>
    public static class ReglasPerfilesNovedades
    {
        public const string VENDEDORES = "Vendedores";
        public const string ALMACEN = "Almacén";
        public const string TIENDAS = "Tiendas";
        public const string ADMINISTRACION = "Administración";
        /// <summary>Se acepta al escribir; se guarda como NULL (para todos).</summary>
        public const string TODOS = "Todos";

        /// <summary>Grupo de dominio de los comerciales (televenta y ruta).</summary>
        internal const string GRUPO_COMERCIALES = "Comerciales";
        /// <summary>El grupo de dominio se llama «Informatica» sin tilde; Constantes lo tiene con tilde.</summary>
        internal const string GRUPO_INFORMATICA_SIN_TILDE = "Informatica";
        /// <summary>Claim que pone ClaimsVendedorHelper en el token de NestoApp.</summary>
        internal const string CLAIM_ES_VENDEDOR = "IsVendedor";

        /// <summary>Longitud de la columna Novedades.Perfiles.</summary>
        public const int LONGITUD_COLUMNA = 200;

        /// <summary>Los perfiles que se pueden elegir, en el orden en que se enseñan.</summary>
        public static readonly IReadOnlyList<string> DISPONIBLES = new[] { VENDEDORES, ALMACEN, TIENDAS, ADMINISTRACION };

        /// <summary>
        /// El nombre canónico del perfil (sin distinguir mayúsculas ni tildes: «almacen» → «Almacén»), o null si no
        /// es ninguno. «Todos» devuelve <see cref="TODOS"/>.
        /// </summary>
        public static string Canonico(string perfil)
        {
            string limpio = SinTildes(perfil?.Trim());
            if (string.IsNullOrEmpty(limpio))
            {
                return null;
            }
            if (string.Equals(limpio, TODOS, StringComparison.OrdinalIgnoreCase))
            {
                return TODOS;
            }
            return DISPONIBLES.FirstOrDefault(p => string.Equals(SinTildes(p), limpio, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Los perfiles de la columna («Almacén,Tiendas»; separados por coma, punto y coma o barra). null = para
        /// todos: columna vacía, con «Todos» o sin ningún perfil conocido (un dato mal escrito no debe esconder la
        /// novedad a todo el mundo).
        /// </summary>
        public static List<string> Parsear(string columna)
        {
            if (string.IsNullOrWhiteSpace(columna))
            {
                return null;
            }
            List<string> canonicos = columna
                .Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(Canonico)
                .Where(p => p != null)
                .ToList();
            if (canonicos.Count == 0 || canonicos.Contains(TODOS))
            {
                return null;
            }
            return Ordenar(canonicos);
        }

        /// <summary>
        /// Valida lo que llega por la API y devuelve el valor de la columna (null = para todos) o, en
        /// <paramref name="error"/>, por qué no vale.
        /// </summary>
        public static string ValorColumna(IEnumerable<string> perfiles, out string error)
        {
            error = null;
            var canonicos = new List<string>();
            foreach (string perfil in (perfiles ?? Enumerable.Empty<string>()).Where(p => !string.IsNullOrWhiteSpace(p)))
            {
                string canonico = Canonico(perfil);
                if (canonico == null)
                {
                    error = $"«{perfil.Trim()}» no es un perfil. Tiene que ser {string.Join(", ", DISPONIBLES)} o {TODOS}";
                    return null;
                }
                canonicos.Add(canonico);
            }
            if (canonicos.Count == 0 || canonicos.Contains(TODOS) || DISPONIBLES.All(canonicos.Contains))
            {
                return null;
            }
            return string.Join(",", Ordenar(canonicos));
        }

        /// <summary>Si la novedad (con esos perfiles) le sale por defecto a ese usuario.</summary>
        public static bool EsParaElUsuario(IReadOnlyCollection<string> perfilesNovedad, PerfilesUsuarioNovedades usuario)
        {
            if (usuario == null || usuario.VeTodas || perfilesNovedad == null || perfilesNovedad.Count == 0)
            {
                return true;
            }
            return perfilesNovedad.Any(p => usuario.Perfiles.Contains(p));
        }

        /// <summary>Los perfiles del usuario según sus grupos de dominio (o roles) y claims (tabla de arriba).</summary>
        public static PerfilesUsuarioNovedades DeducirPerfiles(IPrincipal usuario)
        {
            if (usuario?.Identity == null || !usuario.Identity.IsAuthenticated)
            {
                return PerfilesUsuarioNovedades.Todas(false);
            }
            if (EsDireccionOInformatica(usuario))
            {
                return PerfilesUsuarioNovedades.Todas(true);
            }
            var perfiles = new List<string>();
            if (usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.ALMACEN))
            {
                perfiles.Add(ALMACEN);
            }
            if (usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.TIENDAS))
            {
                perfiles.Add(TIENDAS);
            }
            if (usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.ADMINISTRACION)
                || usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.COMPRAS)
                || usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.TIENDA_ON_LINE))
            {
                perfiles.Add(ADMINISTRACION);
            }
            if (perfiles.Count == 0 && (usuario.IsInRoleSinDominio(GRUPO_COMERCIALES) || TieneClaimVendedor(usuario)))
            {
                perfiles.Add(VENDEDORES);
            }
            return perfiles.Count == 0
                ? PerfilesUsuarioNovedades.Todas(false)
                : new PerfilesUsuarioNovedades { Perfiles = perfiles, VeTodas = false };
        }

        /// <summary>Quien redacta las novedades: ve todas y puede cambiar a quién afecta cada una.</summary>
        public static bool EsDireccionOInformatica(IPrincipal usuario) =>
            usuario != null && (usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.DIRECCION)
                || usuario.IsInRoleSinDominio(GRUPO_INFORMATICA_SIN_TILDE)
                || usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.INFORMATICA));

        private static bool TieneClaimVendedor(IPrincipal usuario) =>
            usuario is ClaimsPrincipal claims
            && claims.Claims.Any(c => c.Type == CLAIM_ES_VENDEDOR && string.Equals(c.Value, "true", StringComparison.OrdinalIgnoreCase));

        private static List<string> Ordenar(IEnumerable<string> perfiles)
        {
            List<string> orden = DISPONIBLES.ToList();
            return perfiles.Distinct().OrderBy(p => orden.IndexOf(p)).ToList();
        }

        private static string SinTildes(string texto)
        {
            if (string.IsNullOrEmpty(texto))
            {
                return texto;
            }
            string descompuesto = texto.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(descompuesto.Length);
            foreach (char c in descompuesto)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                {
                    _ = sb.Append(c);
                }
            }
            return sb.ToString().Normalize(NormalizationForm.FormC);
        }
    }

    /// <summary>
    /// Sugerencia 551: GET api/Novedades/MisPerfiles. Los perfiles con los que se filtran las novedades de quien
    /// pregunta, si las ve todas por su puesto y si puede cambiar a quién afecta cada novedad.
    /// </summary>
    public class PerfilesUsuarioNovedades
    {
        /// <summary>Vacía si <see cref="VeTodas"/>.</summary>
        public List<string> Perfiles { get; set; } = new List<string>();
        /// <summary>Dirección, Informática o sin perfil conocido: no se le filtra nada.</summary>
        public bool VeTodas { get; set; }
        /// <summary>Dirección o Informática: PUT api/Novedades/{id}/Perfiles.</summary>
        public bool PuedeEditar { get; set; }
        /// <summary>Los perfiles que se pueden poner a una novedad (Vendedores, Almacén, Tiendas, Administración).</summary>
        public List<string> Disponibles { get; set; } = ReglasPerfilesNovedades.DISPONIBLES.ToList();

        internal static PerfilesUsuarioNovedades Todas(bool puedeEditar) => new PerfilesUsuarioNovedades { VeTodas = true, PuedeEditar = puedeEditar };
    }
}
