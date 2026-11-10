using NestoAPI.Infraestructure.Sincronizacion;
using NestoAPI.Models;
using NestoAPI.Models.Sincronizacion;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Clientes.Embudo
{
    /// <summary>
    /// Lo que hace la cola de Nesto_sync con cada cliente (job 'sincronizar-clientes' y GET api/Clientes/Sync): sus fechas de
    /// compras una vez por cliente (NestoAPI#498) y TODOS sus contactos. Si el cliente tiene un bloque del embudo pendiente
    /// (odoo-custom-addons#45, dbo.ClientesEmbudo con FechaEnviado NULL), cada contacto viaja con BloqueEmbudo y el responsable de
    /// su código postal, y el bloque se marca enviado cuando se han publicado TODOS. Si la publicación falla a medias, el registro
    /// de Nesto_sync se queda pendiente (ProcesarTabla) y el bloque también: la pasada siguiente los vuelve a mandar con los campos.
    /// Una modificación posterior de la ficha ya va sin ellos.
    /// <para>ProcesarTabla recorre los registros de uno en uno: el estado de esta clase es el del registro en curso.</para>
    /// </summary>
    public class PasadaSincronizacionClientes
    {
        public const string SOURCE = "Nesto viejo";

        private readonly IGestorClientes gestorClientes;
        private readonly Func<string, Task<List<Cliente>>> leerContactos;
        private readonly IRepositorioClientesEmbudo embudo;
        private readonly CalculadorResponsableEmbudo responsables;

        private string cliente;
        private FechasComprasCliente fechasCompras;
        private string bloque;
        private int contactos;
        private int publicados;

        public PasadaSincronizacionClientes(IGestorClientes gestorClientes, Func<string, Task<List<Cliente>>> leerContactos,
            IRepositorioClientesEmbudo embudo, CalculadorResponsableEmbudo responsables)
        {
            this.gestorClientes = gestorClientes;
            this.leerContactos = leerContactos;
            this.embudo = embudo;
            this.responsables = responsables;
        }

        /// <summary>La de producción, con los contactos leídos de <paramref name="db"/>.</summary>
        public static PasadaSincronizacionClientes DeProduccion(NVEntities db, IGestorClientes gestorClientes)
        {
            return new PasadaSincronizacionClientes(gestorClientes, numero => LeerContactos(db, numero),
                new RepositorioClientesEmbudoSql(),
                new CalculadorResponsableEmbudo(new DatosResponsableEmbudo(new ServicioGestorClientes(), new Origenes.DestinatariosLeadsSinLlamar())));
        }

        /// <summary>
        /// NestoAPI#504: PublicarClienteSincronizar lee PersonasContactoClientes (sin el 1; las dos navegaciones cuelgan de la
        /// misma FK duplicada) y el Mail del vendedor: con el lazy loading apagado, lo que no se incluye aquí viaja vacío. El grupo
        /// de producto, para el vendedor de peluquería (odoo-custom-addons#45) sin otra consulta por contacto.
        /// </summary>
        public static Task<List<Cliente>> LeerContactos(NVEntities db, string numero)
        {
            return db.Clientes
                .Where(c => c.Nº_Cliente == numero && c.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO)
                .OrderBy(c => c.Nº_Cliente)
                .ThenByDescending(c => c.ClientePrincipal)
                .ThenBy(c => c.Contacto)
                .Include(c => c.PersonasContactoClientes)
                .Include(c => c.Vendedore)
                .Include(c => c.VendedoresClienteGrupoProductoes)
                .ToListAsync();
        }

        public async Task<List<Cliente>> ObtenerEntidades(NestoSyncRecord registro)
        {
            cliente = registro.ModificadoId?.Trim();
            publicados = 0;
            fechasCompras = CalculoFechasComprasCliente.Buscar(
                await gestorClientes.LeerFechasCompras(new[] { registro.ModificadoId }).ConfigureAwait(false), registro.ModificadoId);
            bloque = await BloquePendiente(cliente).ConfigureAwait(false);
            List<Cliente> entidades = await leerContactos(registro.ModificadoId).ConfigureAwait(false) ?? new List<Cliente>();
            contactos = entidades.Count;
            return entidades;
        }

        public async Task Publicar(Cliente contacto, string usuario)
        {
            Action<ClienteSyncMessage> completar = null;
            if (bloque != null)
            {
                ResponsableEmbudo responsable = await responsables.Calcular(contacto.CodPostal).ConfigureAwait(false);
                string bloqueDelMensaje = bloque;
                completar = m =>
                {
                    m.BloqueEmbudo = bloqueDelMensaje;
                    m.ResponsableEmbudoEmail = responsable.Email;
                    m.ResponsableEmbudoTipo = responsable.Tipo;
                };
            }
            await gestorClientes.PublicarClienteSincronizar(contacto, fechasCompras, SOURCE, usuario, completar).ConfigureAwait(false);
            publicados++;
            if (bloque != null && publicados == contactos)
            {
                // Todos sus contactos han salido con el bloque: las publicaciones siguientes, sin él. Si esto falla, la excepción
                // deja el registro pendiente y se vuelven a mandar con el bloque (Odoo deduplica).
                await embudo.MarcarEnviado(cliente, bloque).ConfigureAwait(false);
                bloque = null;
            }
        }

        /// <summary>Si no se puede leer el registro del embudo, el cliente se publica igual (sin los campos): el bloque sigue
        /// pendiente y saldrá en su siguiente publicación. Un fallo aquí no puede parar la sincronización de todos los clientes.</summary>
        private async Task<string> BloquePendiente(string numero)
        {
            if (embudo == null || string.IsNullOrEmpty(numero))
            {
                return null;
            }
            try
            {
                return (await embudo.BloquePendiente(numero).ConfigureAwait(false))?.Trim();
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"Embudo de Odoo: no se ha podido leer el bloque pendiente del cliente {numero}; se publica sin él: {ex.Message}", ex),
                    SincronizacionJobsService.USUARIO_SISTEMA);
                return null;
            }
        }
    }
}
