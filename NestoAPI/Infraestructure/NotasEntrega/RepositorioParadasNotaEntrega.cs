using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;
using NestoAPI.Infraestructure.Agencias.RepartoPropio;
using NestoAPI.Models;

namespace NestoAPI.Infraestructure.NotasEntrega
{
    /// <summary>NestoAPI#758: un envío (de cualquier agencia) del pedido de la nota.</summary>
    public class EnvioPedidoNota
    {
        public int Numero { get; set; }
        public int Agencia { get; set; }
        public short Estado { get; set; }
        public short Bultos { get; set; }
    }

    /// <summary>NestoAPI#758: a dónde va la parada (la ficha del cliente/contacto del pedido) y el texto de la nota.</summary>
    public class DestinoNotaEntrega
    {
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string Nombre { get; set; }
        public string Direccion { get; set; }
        public string CodPostal { get; set; }
        public string Poblacion { get; set; }
        public string Provincia { get; set; }
        /// <summary>El campo Teléfono de la ficha, con todos los teléfonos separados por «/».</summary>
        public string Telefonos { get; set; }
        public string Vendedor { get; set; }
        /// <summary>Las líneas de texto de la nota, recortadas y juntas (<see cref="ServicioParadasNotaEntrega.TextoNota"/>).</summary>
        public string Texto { get; set; }
    }

    public interface IRepositorioParadasNotaEntrega
    {
        /// <summary>Todos los envíos del pedido (cualquier agencia y estado), del más nuevo al más viejo.</summary>
        Task<List<EnvioPedidoNota>> LeerEnviosDelPedido(int pedido);
        /// <summary>El envío entero (para la etiqueta), o null.</summary>
        Task<EnviosAgencia> LeerEnvio(int numero);
        /// <summary>El destino del pedido (ficha del cliente/contacto) y el texto de sus líneas; null si el pedido o la ficha no existen.</summary>
        Task<DestinoNotaEntrega> LeerDestinoDelPedido(string empresa, int pedido);
        /// <summary>La empresa de la agencia (AgenciasTransporte.Empresa, recortada), o null.</summary>
        Task<string> LeerEmpresaAgencia(int agencia);
        /// <summary>
        /// Graba el envío ya tramitado: lo inserta, le pone el albarán <c>RP{Numero}</c> y deja una fila en EnviosHistoria con
        /// <paramref name="historia"/>, todo junto. Devuelve el número del envío.
        /// </summary>
        Task<int> CrearEnvioTramitado(EnviosAgencia envio, string historia);
    }

    /// <summary>NestoAPI#758: los envíos de las paradas de las notas de entrega, por EF (EnviosAgencia y EnviosHistoria están en el EDMX).</summary>
    public class RepositorioParadasNotaEntrega : IRepositorioParadasNotaEntrega
    {
        internal const string CAMPO_HISTORIA = "Estado";

        private readonly NVEntities db;

        public RepositorioParadasNotaEntrega(NVEntities db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public Task<List<EnvioPedidoNota>> LeerEnviosDelPedido(int pedido)
            => db.EnviosAgencias.AsNoTracking()
                .Where(e => e.Pedido == pedido)
                .OrderByDescending(e => e.Numero)
                .Select(e => new EnvioPedidoNota { Numero = e.Numero, Agencia = e.Agencia, Estado = e.Estado, Bultos = e.Bultos })
                .ToListAsync();

        public async Task<EnviosAgencia> LeerEnvio(int numero)
            => (await db.EnviosAgencias.AsNoTracking().Where(e => e.Numero == numero).ToListAsync().ConfigureAwait(false)).FirstOrDefault();

        public async Task<DestinoNotaEntrega> LeerDestinoDelPedido(string empresa, int pedido)
        {
            var cabeceras = await db.CabPedidoVtas.AsNoTracking()
                .Where(c => c.Empresa == empresa && c.Número == pedido)
                .Select(c => new { c.Nº_Cliente, c.Contacto, c.Vendedor })
                .ToListAsync().ConfigureAwait(false);
            var cabecera = cabeceras.FirstOrDefault();
            if (cabecera == null)
            {
                return null;
            }
            List<Cliente> fichas = await db.Clientes.AsNoTracking()
                .Where(c => c.Empresa == empresa && c.Nº_Cliente == cabecera.Nº_Cliente && c.Contacto == cabecera.Contacto)
                .ToListAsync().ConfigureAwait(false);
            Cliente ficha = fichas.FirstOrDefault();
            if (ficha == null)
            {
                return null;
            }
            int texto = Constantes.TiposLineaVenta.TEXTO;
            List<string> textos = await db.LinPedidoVtas.AsNoTracking()
                .Where(l => l.Empresa == empresa && l.Número == pedido && l.TipoLinea == texto)
                .OrderBy(l => l.Nº_Orden)
                .Select(l => l.Texto)
                .ToListAsync().ConfigureAwait(false);
            return new DestinoNotaEntrega
            {
                Cliente = cabecera.Nº_Cliente?.Trim(),
                Contacto = cabecera.Contacto?.Trim(),
                Nombre = ficha.Nombre?.Trim(),
                Direccion = ficha.Dirección?.Trim(),
                CodPostal = ficha.CodPostal?.Trim(),
                Poblacion = ficha.Población?.Trim(),
                Provincia = ficha.Provincia?.Trim(),
                Telefonos = ficha.Teléfono?.Trim(),
                Vendedor = cabecera.Vendedor?.Trim(),
                Texto = ServicioParadasNotaEntrega.TextoNota(textos)
            };
        }

        public async Task<string> LeerEmpresaAgencia(int agencia)
            => (await db.AgenciasTransportes.AsNoTracking().Where(a => a.Numero == agencia).Select(a => a.Empresa)
                .ToListAsync().ConfigureAwait(false)).FirstOrDefault()?.Trim();

        public async Task<int> CrearEnvioTramitado(EnviosAgencia envio, string historia)
        {
            using (DbContextTransaction transaccion = db.Database.BeginTransaction())
            {
                try
                {
                    _ = db.EnviosAgencias.Add(envio);
                    _ = await db.SaveChangesAsync().ConfigureAwait(false);
                    envio.CodigoBarras = CodigoBultoRepartoPropio.Albaran(envio.Numero);
                    _ = db.EnviosHistorias.Add(new EnvioHistoria
                    {
                        NumeroEnvio = envio.Numero,
                        Campo = CAMPO_HISTORIA,
                        ValorAnterior = string.Empty,
                        Observaciones = historia,
                        Usuario = envio.Usuario,
                        FechaModificacion = DateTime.Now
                    });
                    _ = await db.SaveChangesAsync().ConfigureAwait(false);
                    transaccion.Commit();
                    return envio.Numero;
                }
                catch (Exception)
                {
                    transaccion.RollbackSeguro();
                    // Que el envío a medias no se quede en el contexto compartido con la facturación de rutas.
                    db.Entry(envio).State = EntityState.Detached;
                    throw;
                }
            }
        }
    }
}
