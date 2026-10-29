using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Eventos
{
    /// <summary>
    /// NestoAPI#731: una persona de contacto del cliente (PersonasContactoClientes), para el combo «Asiste» de la inscripción
    /// (GET api/Eventos/PersonasContacto) y la persona que asiste de cada inscripción (InscripcionEventoDTO.PersonaContacto).
    /// </summary>
    public class PersonaContactoEventoDTO
    {
        /// <summary>El contacto (dirección) del cliente al que pertenece la persona.</summary>
        public string Contacto { get; set; }
        /// <summary>PersonasContactoClientes.Número: lo que se manda en InscribirEventoDTO.PersonaContacto y en el PUT.</summary>
        public string Numero { get; set; }
        public string Nombre { get; set; }
        public string Correo { get; set; }
        public string Telefono { get; set; }
    }

    /// <summary>NestoAPI#731: PUT api/Eventos/Inscripciones/{id}/PersonaContacto. Null o vacío = quitarla.</summary>
    public class CambiarPersonaContactoInscripcionDTO
    {
        public string PersonaContacto { get; set; }
    }

    /// <summary>
    /// NestoAPI#731: la columna EventosInscripciones.PersonaContacto (Scripts/Issue731_EventosInscripcionesPersonaContacto.sql),
    /// fuera del EDMX. Sin la columna: no lee nada y no guarda nada (la API funciona como antes).
    /// </summary>
    public interface IRepositorioPersonaAsistenteEventos
    {
        /// <summary>Inscripción → Número de la persona que asiste (solo las que la tienen).</summary>
        Task<Dictionary<int, string>> Leer(IReadOnlyCollection<int> inscripciones);
        /// <summary>Guarda (o quita, con null) la persona. False si la columna todavía no existe.</summary>
        Task<bool> Poner(int inscripcion, string numero);
    }

    public class RepositorioPersonaAsistenteEventos : IRepositorioPersonaAsistenteEventos
    {
        internal const string SQL_EXISTE = "SELECT CAST(CASE WHEN COL_LENGTH('dbo.EventosInscripciones', 'PersonaContacto') IS NULL THEN 0 ELSE 1 END AS bit)";
        internal const string SQL_LEER = "SELECT Id, RTRIM(PersonaContacto) AS PersonaContacto FROM dbo.EventosInscripciones WITH (NOLOCK) " +
            "WHERE PersonaContacto IS NOT NULL AND Id IN ({0})";
        internal const string SQL_PONER = "UPDATE dbo.EventosInscripciones SET PersonaContacto = @persona WHERE Id = @id";

        private static readonly ComprobadorEsquemaEventos columna = new ComprobadorEsquemaEventos(SQL_EXISTE);
        private readonly NVEntities db;

        public RepositorioPersonaAsistenteEventos(NVEntities db)
        {
            this.db = db;
        }

        public async Task<Dictionary<int, string>> Leer(IReadOnlyCollection<int> inscripciones)
        {
            List<int> ids = (inscripciones ?? new List<int>()).Distinct().ToList();
            if (!ids.Any() || !columna.Existe(db))
            {
                return new Dictionary<int, string>();
            }
            var resultado = new Dictionary<int, string>();
            // Por tandas: la lista de inscritos de un evento es corta, pero LeerInscripcionesCliente puede traer muchas
            foreach (List<int> tanda in Tandas(ids, 500))
            {
                List<FilaPersona> filas = await db.Database
                    .SqlQuery<FilaPersona>(string.Format(SQL_LEER, string.Join(",", tanda)))
                    .ToListAsync().ConfigureAwait(false);
                foreach (FilaPersona fila in filas.Where(f => !string.IsNullOrWhiteSpace(f.PersonaContacto)))
                {
                    resultado[fila.Id] = fila.PersonaContacto.Trim();
                }
            }
            return resultado;
        }

        public async Task<bool> Poner(int inscripcion, string numero)
        {
            if (!columna.Existe(db))
            {
                return false;
            }
            string limpio = string.IsNullOrWhiteSpace(numero) ? null : numero.Trim();
            _ = await db.Database.ExecuteSqlCommandAsync(SQL_PONER,
                new SqlParameter("@persona", SqlDbType.Char, 3) { Value = (object)limpio ?? DBNull.Value },
                new SqlParameter("@id", SqlDbType.Int) { Value = inscripcion }).ConfigureAwait(false);
            return true;
        }

        internal static IEnumerable<List<int>> Tandas(List<int> ids, int tamano)
        {
            for (int i = 0; i < ids.Count; i += tamano)
            {
                yield return ids.Skip(i).Take(tamano).ToList();
            }
        }

        private class FilaPersona
        {
            public int Id { get; set; }
            public string PersonaContacto { get; set; }
        }
    }

    /// <summary>
    /// NestoAPI#731/#732: ¿existe ya la tabla o columna de un script? Se cachea el «sí» para siempre y el «no» cinco minutos
    /// (como RepositorioSuPedidoObligatorio): en cuanto se lanza el script, la API lo ve sin reiniciar.
    /// </summary>
    internal class ComprobadorEsquemaEventos
    {
        private static readonly TimeSpan cadaCuanto = TimeSpan.FromMinutes(5);
        private readonly object candado = new object();
        private readonly string sql;
        private bool existe;
        private DateTime ultimaComprobacion = DateTime.MinValue;

        public ComprobadorEsquemaEventos(string sql)
        {
            this.sql = sql;
        }

        public bool Existe(NVEntities db)
        {
            lock (candado)
            {
                if (existe || DateTime.Now - ultimaComprobacion < cadaCuanto)
                {
                    return existe;
                }
                ultimaComprobacion = DateTime.Now;
            }
            bool ahora;
            try
            {
                ahora = db.Database.SqlQuery<bool>(sql).FirstOrDefault();
            }
            catch
            {
                ahora = false;
            }
            lock (candado)
            {
                existe = ahora;
            }
            return ahora;
        }
    }

    /// <summary>
    /// NestoAPI#731: lo que la inscripción enseña de la ficha, solo lectura (no se guarda): el correo y la dirección.
    /// </summary>
    public static class FichaInscripcionEvento
    {
        /// <summary>
        /// El correo de la persona que asiste si lo tiene; si no, el de alguna persona de contacto de la misma dirección; si no,
        /// el de cualquier persona del cliente (que es donde lo buscan los correos automáticos, como en la sugerencia 776).
        /// Solo correos válidos; con varios separados por «;» o «,», el primero válido.
        /// </summary>
        public static string Correo(string contacto, string personaAsiste, IEnumerable<PersonaContactoEventoDTO> personasDelCliente)
        {
            List<PersonaContactoEventoDTO> personas = (personasDelCliente ?? Enumerable.Empty<PersonaContactoEventoDTO>())
                .Where(p => p != null).ToList();
            string contactoLimpio = contacto?.Trim();
            string asiste = personaAsiste?.Trim();
            IEnumerable<PersonaContactoEventoDTO> candidatas = personas
                .Where(p => p.Contacto == contactoLimpio && !string.IsNullOrEmpty(asiste) && p.Numero == asiste)
                .Concat(personas.Where(p => p.Contacto == contactoLimpio))
                .Concat(personas);
            foreach (PersonaContactoEventoDTO persona in candidatas)
            {
                string correo = PrimerCorreoValido(persona.Correo);
                if (correo != null)
                {
                    return correo;
                }
            }
            return null;
        }

        /// <summary>«Calle Mayor 1, 28001 Madrid (Madrid)»: dirección, código postal, población y provincia de la ficha del
        /// contacto. Null si la ficha no tiene nada.</summary>
        public static string Direccion(string direccion, string codigoPostal, string poblacion, string provincia)
        {
            string calle = direccion?.Trim();
            string lugar = string.Join(" ", new[] { codigoPostal?.Trim(), poblacion?.Trim() }.Where(s => !string.IsNullOrEmpty(s)));
            string prov = provincia?.Trim();
            if (!string.IsNullOrEmpty(prov))
            {
                lugar = string.IsNullOrEmpty(lugar) ? prov : $"{lugar} ({prov})";
            }
            string resultado = string.Join(", ", new[] { calle, lugar }.Where(s => !string.IsNullOrEmpty(s)));
            return string.IsNullOrEmpty(resultado) ? null : resultado;
        }

        internal static string PrimerCorreoValido(string correos)
        {
            if (string.IsNullOrWhiteSpace(correos))
            {
                return null;
            }
            return correos.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(c => c.Trim())
                .FirstOrDefault(c => PedidosVenta.ComercialesPedido.EsCorreoValido(c));
        }
    }
}
