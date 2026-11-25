using NestoAPI.Infrastructure;
using NestoAPI.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Principal;

namespace NestoAPI.Infraestructure.Campanas.OfertasParaTi
{
    /// <summary>La petición ya validada, con los valores por defecto puestos.</summary>
    public class PeticionPushCampanaValida
    {
        public string Campana { get; set; }
        public string Titulo { get; set; }
        public string Cuerpo { get; set; }
        public string ImagenUrl { get; set; }
        public int Maximo { get; set; }
        public string Modo { get; set; }
        public ISet<string> SoloClientes { get; set; }
        public ISet<string> ExcluirClientes { get; set; }
        public int DiasSinRepetir { get; set; }
        public bool IncluirSinOfertas { get; set; }
        public string CuerpoSinOfertas { get; set; }
    }

    /// <summary>
    /// «Ofertas para ti», la push de campaña (POST api/Campanas/Push): reglas puras de permisos, petición, destinatarios y
    /// anti-saturación.
    /// </summary>
    public static class ReglaPushCampana
    {
        public const int DIAS_SIN_REPETIR_POR_DEFECTO = 2;
        public const int DIAS_SIN_REPETIR_TOPE = 60;
        public const int LONGITUD_MAXIMA_TITULO = 100;
        public const int LONGITUD_MAXIMA_CUERPO = 250;

        public const string MENSAJE_SIN_PERMISO = "Las push de campaña a clientes solo las pueden lanzar Dirección e Informática.";
        public const string MENSAJE_SIN_CAMPANA = "Indique la campaña (el nombre que tiene en DescuentosProducto).";
        public const string MENSAJE_FUERA_DE_HORARIO = "De verdad solo se mandan push a clientes de 9:00 a 21:00. Pruebe en ensayo o vuelva a lanzarla en horario.";

        public const string MOTIVO_DE_BAJA = "Ha apagado «Avisos de reposición» en la app.";
        public const string MOTIVO_EXCLUIDO = "Excluido en la petición.";
        public const string MOTIVO_SIN_APP = "No tiene la app con los consejos encendidos.";
        public const string MOTIVO_SIN_OFERTAS = "Sin ofertas para él (y la petición no incluye a los que no tienen).";

        public static string MotivoRepetido(string campana, int dias)
            => dias == 1
                ? $"Ya recibió hoy una push de «{campana}»."
                : $"Ya recibió una push de «{campana}» en los últimos {dias} días.";

        /// <summary>Dirección e Informática.</summary>
        public static bool PuedeLanzar(IPrincipal usuario)
            => usuario != null && (usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.DIRECCION)
                || usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.INFORMATICA));

        /// <summary>El mensaje del 400 (de usted) o null si vale; en <paramref name="valida"/>, la petición con lo que falta por defecto.</summary>
        public static string Validar(PeticionCampanaPushDTO peticion, out PeticionPushCampanaValida valida)
        {
            valida = null;
            if (peticion == null || string.IsNullOrWhiteSpace(peticion.Campana))
            {
                return MENSAJE_SIN_CAMPANA;
            }
            string modo = string.IsNullOrWhiteSpace(peticion.Modo) ? ModosPushCampana.OFERTAS : peticion.Modo.Trim();
            if (string.Equals(modo, ModosPushCampana.OFERTAS, StringComparison.OrdinalIgnoreCase))
            {
                modo = ModosPushCampana.OFERTAS;
            }
            else if (string.Equals(modo, ModosPushCampana.CARRITO, StringComparison.OrdinalIgnoreCase))
            {
                modo = ModosPushCampana.CARRITO;
            }
            else
            {
                return $"El modo «{peticion.Modo}» no existe: use «{ModosPushCampana.OFERTAS}» o «{ModosPushCampana.CARRITO}».";
            }
            if (peticion.Maximo.HasValue && (peticion.Maximo < 1 || peticion.Maximo > ReglaOfertasParaTi.MAXIMO_TOPE))
            {
                return $"El máximo de productos tiene que estar entre 1 y {ReglaOfertasParaTi.MAXIMO_TOPE}.";
            }
            int dias = peticion.DiasSinRepetir ?? DIAS_SIN_REPETIR_POR_DEFECTO;
            if (dias < 0 || dias > DIAS_SIN_REPETIR_TOPE)
            {
                return $"Los días sin repetir tienen que estar entre 0 y {DIAS_SIN_REPETIR_TOPE}.";
            }
            if ((peticion.Titulo?.Trim().Length ?? 0) > LONGITUD_MAXIMA_TITULO)
            {
                return $"El título no puede pasar de {LONGITUD_MAXIMA_TITULO} caracteres.";
            }
            if ((peticion.Cuerpo?.Trim().Length ?? 0) > LONGITUD_MAXIMA_CUERPO || (peticion.CuerpoSinOfertas?.Trim().Length ?? 0) > LONGITUD_MAXIMA_CUERPO)
            {
                return $"El texto no puede pasar de {LONGITUD_MAXIMA_CUERPO} caracteres.";
            }
            if (!string.IsNullOrWhiteSpace(peticion.ImagenUrl) && CorreosPostCompra.PlantillaRecordatorioReposicion.NormalizarUrlFoto(peticion.ImagenUrl) == null)
            {
                return "La imagen tiene que ser una dirección web completa (https://…).";
            }
            valida = new PeticionPushCampanaValida
            {
                Campana = peticion.Campana.Trim(),
                Titulo = Limpio(peticion.Titulo),
                Cuerpo = Limpio(peticion.Cuerpo),
                ImagenUrl = Limpio(peticion.ImagenUrl),
                Maximo = ReglaOfertasParaTi.Maximo(peticion.Maximo),
                Modo = modo,
                SoloClientes = Conjunto(peticion.SoloClientes),
                ExcluirClientes = Conjunto(peticion.ExcluirClientes),
                DiasSinRepetir = dias,
                IncluirSinOfertas = peticion.IncluirSinOfertas ?? true,
                CuerpoSinOfertas = Limpio(peticion.CuerpoSinOfertas)
            };
            return null;
        }

        /// <summary>Lo que se compara en el buzón: el par «campana» del JSON de Datos tal y como lo escribe el servicio de push.</summary>
        public static string MarcaCampana(string campana) => "\"campana\":" + JsonConvert.SerializeObject(campana?.Trim());

        /// <summary>Desde qué día cuenta una push anterior: con 2 días, la de ayer cuenta y la de anteayer ya no. Null con 0 (sin control).</summary>
        public static DateTime? DesdeSinRepetir(DateTime hoy, int dias)
            => dias <= 0 ? (DateTime?)null : hoy.Date.AddDays(-(dias - 1));

        /// <summary>Solo de verdad, y solo en la franja de las push a clientes (la del recordatorio de reposición).</summary>
        public static bool EnHorario(DateTime ahora)
            => ahora.TimeOfDay >= CorreosPostCompra.RecordatorioReposicionPushJobsService.FRANJA_DESDE
                && ahora.TimeOfDay < CorreosPostCompra.RecordatorioReposicionPushJobsService.FRANJA_HASTA;

        /// <summary>El reparto de destinatarios.</summary>
        public class Reparto
        {
            public List<string> Destinatarios { get; set; } = new List<string>();
            public List<SaltadoPushCampanaDTO> Saltados { get; set; } = new List<SaltadoPushCampanaDTO>();
        }

        /// <summary>
        /// Con la app y los consejos encendidos, sin baja en «Avisos de reposición» (el criterio del recordatorio de reposición, la push
        /// comercial que ya existía), dentro de SoloClientes si viene, fuera de ExcluirClientes y sin una push de esta campaña reciente.
        /// </summary>
        public static Reparto Repartir(IEnumerable<string> conApp, ISet<string> deBaja, PeticionPushCampanaValida peticion, ISet<string> conPushReciente)
        {
            var reparto = new Reparto();
            var app = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var orden = new List<string>();
            foreach (string c in conApp ?? Enumerable.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(c) && app.Add(c.Trim()))
                {
                    orden.Add(c.Trim());
                }
            }
            ISet<string> solo = peticion?.SoloClientes ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            ISet<string> excluir = peticion?.ExcluirClientes ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            ISet<string> baja = Conjunto(deBaja);
            ISet<string> recientes = Conjunto(conPushReciente);
            if (solo.Count > 0)
            {
                foreach (string s in solo.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).Where(s => !app.Contains(s)))
                {
                    reparto.Saltados.Add(new SaltadoPushCampanaDTO { Cliente = s, Motivo = MOTIVO_SIN_APP });
                }
                orden = orden.Where(solo.Contains).ToList();
            }
            foreach (string cliente in orden)
            {
                string motivo = excluir.Contains(cliente) ? MOTIVO_EXCLUIDO
                    : baja.Contains(cliente) ? MOTIVO_DE_BAJA
                    : recientes.Contains(cliente) ? MotivoRepetido(peticion?.Campana, peticion?.DiasSinRepetir ?? 0)
                    : null;
                if (motivo == null)
                {
                    reparto.Destinatarios.Add(cliente);
                }
                else
                {
                    reparto.Saltados.Add(new SaltadoPushCampanaDTO { Cliente = cliente, Motivo = motivo });
                }
            }
            return reparto;
        }

        /// <summary>En modo Carrito solo van los habituales del cliente (nunca sugerencias nuevas): la app ya publicada los mete al carrito.</summary>
        public static List<OfertaElegida> ProductosDelModo(string modo, IEnumerable<OfertaElegida> ofertas)
        {
            List<OfertaElegida> lista = (ofertas ?? Enumerable.Empty<OfertaElegida>()).Where(o => o?.Candidata != null).ToList();
            return string.Equals(modo, ModosPushCampana.CARRITO, StringComparison.OrdinalIgnoreCase)
                ? lista.Where(o => o.Habitual).ToList()
                : lista;
        }

        internal static ISet<string> Conjunto(IEnumerable<string> valores)
            => new HashSet<string>((valores ?? Enumerable.Empty<string>()).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()),
                StringComparer.OrdinalIgnoreCase);

        private static string Limpio(string texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
    }
}
