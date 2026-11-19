using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Clientes
{
    /// <summary>NestoAPI#879 (corte 3): una ficha (contacto) del cliente, lo justo para decidir la entrega por defecto.</summary>
    public class FichaContactoDefecto
    {
        public string Contacto { get; set; }
        public short? Estado { get; set; }
        public bool ClientePrincipal { get; set; }
        /// <summary>Clientes.ContactoDefecto de esta ficha (solo cuenta el de la principal).</summary>
        public string ContactoDefecto { get; set; }
    }

    /// <summary>
    /// NestoAPI#879 (corte 3, Carlos 19/11/26): LA REGLA de datos de la entrega por defecto. La dirección de FACTURACIÓN es la ficha con
    /// ClientePrincipal; la de ENTREGA POR DEFECTO es el contacto que dice el ContactoDefecto de la ficha principal. Pura.
    /// </summary>
    public static class ReglaContactoDefecto
    {
        internal static bool EstaActiva(FichaContactoDefecto ficha) => ficha?.Estado >= Constantes.Clientes.Estados.VISITA_PRESENCIAL;

        /// <summary>
        /// Las fichas principales activas (Estado ≥ 0). Normalmente una; si hubiera varias (marca mal puesta), se escriben todas para que
        /// quien lea cualquiera (p. ej. GET PlantillaVentas/DireccionesEntrega, que coge la primera) vea lo mismo.
        /// </summary>
        public static List<FichaContactoDefecto> PrincipalesActivas(IEnumerable<FichaContactoDefecto> fichas) =>
            (fichas ?? Enumerable.Empty<FichaContactoDefecto>())
                .Where(f => f != null && f.ClientePrincipal && EstaActiva(f))
                .OrderBy(f => f.Contacto?.Trim(), StringComparer.Ordinal)
                .ToList();

        /// <summary>La principal que manda al leer: la primera activa (mismo orden que GET api/Clientes sin contacto), o null.</summary>
        public static FichaContactoDefecto Principal(IEnumerable<FichaContactoDefecto> fichas) => PrincipalesActivas(fichas).FirstOrDefault();

        /// <summary>El contacto de entrega por defecto (recortado), o null si la principal no lo tiene.</summary>
        public static string ContactoEntrega(IEnumerable<FichaContactoDefecto> fichas)
        {
            string contacto = Principal(fichas)?.ContactoDefecto?.Trim();
            return string.IsNullOrEmpty(contacto) ? null : contacto;
        }

        /// <summary>Por qué no se puede poner <paramref name="contacto"/> como entrega por defecto (de usted), o null si se puede.</summary>
        public static string MotivoParaNoCambiar(IEnumerable<FichaContactoDefecto> fichas, string cliente, string contacto)
        {
            List<FichaContactoDefecto> lista = (fichas ?? Enumerable.Empty<FichaContactoDefecto>()).Where(f => f != null).ToList();
            string numero = cliente?.Trim();
            string elegido = contacto?.Trim();
            if (!lista.Any())
            {
                return $"No existe el cliente {numero}.";
            }
            if (string.IsNullOrEmpty(elegido))
            {
                return "Indique el contacto que será la dirección de entrega por defecto.";
            }
            FichaContactoDefecto ficha = lista.FirstOrDefault(f => string.Equals(f.Contacto?.Trim(), elegido, StringComparison.OrdinalIgnoreCase));
            if (ficha == null)
            {
                return $"El contacto {elegido} no es del cliente {numero}.";
            }
            if (!EstaActiva(ficha))
            {
                return $"El contacto {elegido} del cliente {numero} está anulado: no puede ser la dirección de entrega por defecto.";
            }
            if (!PrincipalesActivas(lista).Any())
            {
                return $"El cliente {numero} no tiene una ficha principal activa (la de facturación): no se puede elegir la dirección de entrega por defecto.";
            }
            return null;
        }

        /// <summary>True si ya es la entrega por defecto en TODAS las principales activas (entonces no se escribe ni se deja rastro).</summary>
        public static bool YaEsLaDeEntrega(IEnumerable<FichaContactoDefecto> fichas, string contacto)
        {
            List<FichaContactoDefecto> principales = PrincipalesActivas(fichas);
            return principales.Any() && principales.All(p => string.Equals(p.ContactoDefecto?.Trim(), contacto?.Trim(), StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>NestoAPI#879 (corte 3): lectura de las fichas del cliente y escritura del ContactoDefecto (sustituible en los tests).</summary>
    public interface IRepositorioContactoDefecto
    {
        Task<List<FichaContactoDefecto>> LeerFichas(string empresa, string cliente);

        /// <summary>
        /// Escribe <paramref name="contactoNuevo"/> en el ContactoDefecto de esas fichas principales y deja UNA fila en Modificaciones,
        /// todo en una transacción.
        /// </summary>
        Task Guardar(string empresa, string cliente, IEnumerable<string> contactosPrincipales, string contactoAnterior, string contactoNuevo, string usuario);
    }

    public class RepositorioContactoDefecto : IRepositorioContactoDefecto
    {
        /// <summary>Tabla del rastro en Modificaciones.</summary>
        public const string TABLA_MODIFICACIONES = "ClienteContactoDefecto";

        // Una fila por ficha principal (normalmente una): trgClientesUpd encola la sincronización de ESA ficha. Nunca en masa.
        internal const string SQL_GUARDAR = @"
            UPDATE dbo.Clientes SET ContactoDefecto = @p3, Usuario = @p4
            WHERE Empresa = @p0 AND [Nº Cliente] = @p1 AND Contacto = @p2";

        internal const string SQL_RASTRO = @"
            INSERT INTO dbo.Modificaciones (Tabla, Anterior, Nuevo, Usuario) VALUES (@p0, @p1, @p2, @p3)";

        public async Task<List<FichaContactoDefecto>> LeerFichas(string empresa, string cliente)
        {
            using (var db = new NVEntities())
            {
                db.Configuration.LazyLoadingEnabled = false;
                db.Configuration.ProxyCreationEnabled = false;
                return await db.Clientes
                    .Where(c => c.Empresa == empresa && c.Nº_Cliente == cliente)
                    .Select(c => new FichaContactoDefecto
                    {
                        Contacto = c.Contacto,
                        Estado = c.Estado,
                        ClientePrincipal = c.ClientePrincipal,
                        ContactoDefecto = c.ContactoDefecto
                    })
                    .ToListAsync().ConfigureAwait(false);
            }
        }

        public async Task Guardar(string empresa, string cliente, IEnumerable<string> contactosPrincipales, string contactoAnterior, string contactoNuevo, string usuario)
        {
            string quien = UsuarioAuditoriaHelper.ParaAuditoria(usuario);
            using (var db = new NVEntities())
            using (var transaccion = db.Database.BeginTransaction())
            {
                foreach (string principal in contactosPrincipales)
                {
                    _ = await db.Database.ExecuteSqlCommandAsync(SQL_GUARDAR, empresa, cliente, principal, contactoNuevo, quien).ConfigureAwait(false);
                }
                _ = await db.Database.ExecuteSqlCommandAsync(SQL_RASTRO, TABLA_MODIFICACIONES,
                    TextoRastro(empresa, cliente, contactoAnterior), TextoRastro(empresa, cliente, contactoNuevo), quien).ConfigureAwait(false);
                transaccion.Commit();
            }
        }

        /// <summary>«Empresa 1 · cliente 42143 · entrega por defecto: contacto 1» (o «sin contacto»). Puro.</summary>
        internal static string TextoRastro(string empresa, string cliente, string contacto) =>
            $"Empresa {empresa?.Trim()} · cliente {cliente?.Trim()} · entrega por defecto: {(string.IsNullOrWhiteSpace(contacto) ? "sin contacto" : "contacto " + contacto.Trim())}";
    }
}
