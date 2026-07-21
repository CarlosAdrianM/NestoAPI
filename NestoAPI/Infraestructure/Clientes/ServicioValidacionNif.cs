using NestoAPI.Models;
using NestoAPI.Models.Clientes;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Clientes
{
    /// <summary>
    /// NestoAPI#327: valida el NIF de las fichas contra el censo de la AEAT (VNifV2) y cachea
    /// el veredicto en ValidacionesNif. El estado efectivo compara la ficha ACTUAL con lo que
    /// se validó: si el NIF o el nombre cambiaron después, vuelve a "sin validar" solo.
    /// </summary>
    public class ServicioValidacionNif : IServicioValidacionNif
    {
        public const string ESTADO_CORRECTO = "CORRECTO";
        public const string ESTADO_INCORRECTO = "INCORRECTO";

        private static readonly HashSet<string> ClientesSimplificadas = new HashSet<string>
        {
            Constantes.ClientesEspeciales.AMAZON,
            Constantes.ClientesEspeciales.TIENDA_ONLINE,
            Constantes.ClientesEspeciales.PUBLICO_FINAL
        };

        private readonly NVEntities db;
        private readonly IAlmacenValidacionesNif almacen;
        private readonly IServicioGestorClientes servicioAeat;

        public ServicioValidacionNif(NVEntities db, IAlmacenValidacionesNif almacen = null,
            IServicioGestorClientes servicioAeat = null)
        {
            this.db = db;
            this.almacen = almacen ?? new AlmacenValidacionesNif(db);
            this.servicioAeat = servicioAeat ?? new ServicioGestorClientes();
        }

        public async Task<ResultadoValidacionNif> ObtenerEstado(string empresa, string cliente, string contacto)
        {
            Cliente ficha = await LeerFicha(empresa, cliente, contacto).ConfigureAwait(false);
            if (ficha == null)
            {
                return new ResultadoValidacionNif { Estado = EstadoValidacionNif.SinValidar };
            }
            return await CalcularEstado(ficha).ConfigureAwait(false);
        }

        public async Task<ResultadoValidacionNif> ValidarSiHaceFalta(string empresa, string cliente, string contacto, string usuario)
        {
            Cliente ficha = await LeerFicha(empresa, cliente, contacto).ConfigureAwait(false);
            if (ficha == null)
            {
                return new ResultadoValidacionNif { Estado = EstadoValidacionNif.SinValidar };
            }

            ResultadoValidacionNif estadoActual = await CalcularEstado(ficha).ConfigureAwait(false);
            if (estadoActual.Estado != EstadoValidacionNif.SinValidar)
            {
                return estadoActual; // cacheado (o excluido): no se vuelve a preguntar a la AEAT
            }

            string nif = ficha.CIF_NIF?.Trim();
            string nombre = ficha.Nombre?.Trim();
            if (string.IsNullOrWhiteSpace(nif))
            {
                // Sin NIF no hay nada que validar contra el censo: se queda sin validar
                // (la factura F1 fallará por otra validación; las simplificadas están excluidas).
                return estadoActual;
            }

            RespuestaNifNombreCliente respuesta;
            try
            {
                respuesta = await servicioAeat.ComprobarNifNombre(nif, nombre).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // La AEAT no responde: no se bloquea nada ni se cachea nada; se reintentará
                // en el siguiente pedido/factura. Best-effort con traza.
                ElmahHelper.Log(new Exception(
                    $"ValidacionNif: VNifV2 no disponible al validar {nif} del cliente {cliente?.Trim()}/{contacto?.Trim()}: {ex.Message}", ex));
                return estadoActual;
            }

            var registro = new ValidacionNifRegistro
            {
                Empresa = empresa?.Trim(),
                Cliente = cliente?.Trim(),
                Contacto = contacto?.Trim(),
                Nif = nif,
                Nombre = nombre,
                Estado = respuesta.NifValidado ? ESTADO_CORRECTO : ESTADO_INCORRECTO,
                ResultadoAeat = respuesta.ResultadoAeat,
                FechaValidacion = DateTime.Now,
                Usuario = usuario
            };
            await almacen.Guardar(registro).ConfigureAwait(false);

            return new ResultadoValidacionNif
            {
                Estado = respuesta.NifValidado ? EstadoValidacionNif.Correcto : EstadoValidacionNif.Incorrecto,
                Nif = nif,
                Nombre = nombre,
                ResultadoAeat = respuesta.ResultadoAeat,
                AcabaDeResultarIncorrecto = !respuesta.NifValidado
            };
        }

        public async Task<ResultadoValidacionNif> ValidarPrincipal(string cliente, string usuario)
        {
            Cliente principal = await db.Clientes.FirstOrDefaultAsync(c =>
                c.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO
                && c.Nº_Cliente == cliente && c.ClientePrincipal)
                .ConfigureAwait(false);
            if (principal == null)
            {
                // Integridad de ClientePrincipal aparte (#331): sin principal no se valida nada.
                return new ResultadoValidacionNif { Estado = EstadoValidacionNif.SinValidar };
            }
            return await ValidarSiHaceFalta(principal.Empresa, principal.Nº_Cliente, principal.Contacto, usuario)
                .ConfigureAwait(false);
        }

        public async Task<ResultadoCorreccionNif> CorregirNif(string cliente, string nifNuevo, string usuario)
        {
            nifNuevo = nifNuevo?.Trim().ToUpper();
            if (string.IsNullOrWhiteSpace(nifNuevo))
            {
                return new ResultadoCorreccionNif { Corregido = false, Motivo = "El NIF no puede estar vacío." };
            }
            if (ClientesSimplificadas.Contains(cliente?.Trim()))
            {
                return new ResultadoCorreccionNif { Corregido = false, Motivo = "Los clientes de facturas simplificadas no llevan NIF real." };
            }

            List<Cliente> fichas = await db.Clientes
                .Where(c => c.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && c.Nº_Cliente == cliente)
                .ToListAsync().ConfigureAwait(false);
            Cliente principal = fichas.FirstOrDefault(c => c.ClientePrincipal) ?? fichas.FirstOrDefault();
            if (principal == null)
            {
                return new ResultadoCorreccionNif { Corregido = false, Motivo = $"No existe el cliente {cliente?.Trim()}." };
            }

            // Validar contra la AEAT ANTES de tocar nada: si Hacienda lo rechaza, la ficha se
            // queda como está (no vamos a sustituir un NIF malo por otro).
            RespuestaNifNombreCliente respuesta = await servicioAeat
                .ComprobarNifNombre(nifNuevo, principal.Nombre?.Trim()).ConfigureAwait(false);
            if (!respuesta.NifValidado)
            {
                return new ResultadoCorreccionNif
                {
                    Corregido = false,
                    Nif = nifNuevo,
                    ResultadoAeat = respuesta.ResultadoAeat,
                    Motivo = $"La AEAT no reconoce el NIF {nifNuevo} para '{principal.Nombre?.Trim()}' " +
                        $"({respuesta.ResultadoAeat ?? "NO IDENTIFICADO"}). No se ha modificado nada."
                };
            }

            // Propagar a TODOS los contactos (#330: todos los contactos comparten NIF) y
            // auditar: estamos modificando un dato fiscal.
            int actualizados = 0;
            foreach (Cliente ficha in fichas)
            {
                if (ficha.CIF_NIF?.Trim() != nifNuevo)
                {
                    _ = db.Modificaciones.Add(new Modificacion
                    {
                        Tabla = "Clientes",
                        Anterior = $"Cliente {ficha.Nº_Cliente?.Trim()}/{ficha.Contacto?.Trim()} CIF_NIF={ficha.CIF_NIF?.Trim()}",
                        Nuevo = $"CIF_NIF={nifNuevo} (corrección centralizada #327, AEAT: {respuesta.ResultadoAeat})",
                        Usuario = usuario
                    });
                    ficha.CIF_NIF = nifNuevo;
                    actualizados++;
                }
            }
            _ = await db.SaveChangesAsync().ConfigureAwait(false);

            // Registrar la validación del principal (es el NIF que se declara al facturar).
            await almacen.Guardar(new ValidacionNifRegistro
            {
                Empresa = principal.Empresa?.Trim(),
                Cliente = principal.Nº_Cliente?.Trim(),
                Contacto = principal.Contacto?.Trim(),
                Nif = nifNuevo,
                Nombre = principal.Nombre?.Trim(),
                Estado = ESTADO_CORRECTO,
                ResultadoAeat = respuesta.ResultadoAeat,
                FechaValidacion = DateTime.Now,
                Usuario = usuario
            }).ConfigureAwait(false);

            return new ResultadoCorreccionNif
            {
                Corregido = true,
                Nif = nifNuevo,
                ResultadoAeat = respuesta.ResultadoAeat,
                NombreAeat = respuesta.NombreFormateado,
                ContactosActualizados = actualizados
            };
        }

        public async Task<int> UnificarNifContactos(string cliente, string usuario)
        {
            if (ClientesSimplificadas.Contains(cliente?.Trim()))
            {
                return 0;
            }

            List<Cliente> fichas = await db.Clientes
                .Where(c => c.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && c.Nº_Cliente == cliente)
                .ToListAsync().ConfigureAwait(false);
            Cliente principal = fichas.FirstOrDefault(c => c.ClientePrincipal);
            string nifPrincipal = principal?.CIF_NIF?.Trim();
            if (principal == null || string.IsNullOrWhiteSpace(nifPrincipal))
            {
                return 0; // sin principal (integridad #331) o sin NIF: nada que propagar
            }

            // Regla de #330: SOLO se propaga un NIF con veredicto CORRECTO de la AEAT.
            ResultadoValidacionNif estadoPrincipal = await CalcularEstado(principal).ConfigureAwait(false);
            if (estadoPrincipal.Estado != EstadoValidacionNif.Correcto)
            {
                return 0;
            }

            int corregidos = 0;
            foreach (Cliente ficha in fichas.Where(f => !f.ClientePrincipal && f.CIF_NIF?.Trim() != nifPrincipal))
            {
                // Auditar: se modifica un dato fiscal sin intervención humana (#330).
                _ = db.Modificaciones.Add(new Modificacion
                {
                    Tabla = "Clientes",
                    Anterior = $"Cliente {ficha.Nº_Cliente?.Trim()}/{ficha.Contacto?.Trim()} CIF_NIF={ficha.CIF_NIF?.Trim()}",
                    Nuevo = $"CIF_NIF={nifPrincipal} (propagado del principal validado contra la AEAT, #330)",
                    Usuario = usuario
                });
                ficha.CIF_NIF = nifPrincipal;
                corregidos++;
            }
            if (corregidos > 0)
            {
                _ = await db.SaveChangesAsync().ConfigureAwait(false);
            }
            return corregidos;
        }

        private async Task<Cliente> LeerFicha(string empresa, string cliente, string contacto)
        {
            return await db.Clientes.FirstOrDefaultAsync(c =>
                c.Empresa == empresa && c.Nº_Cliente == cliente && c.Contacto == contacto)
                .ConfigureAwait(false);
        }

        private async Task<ResultadoValidacionNif> CalcularEstado(Cliente ficha)
        {
            string nif = ficha.CIF_NIF?.Trim();
            string nombre = ficha.Nombre?.Trim();
            var resultado = new ResultadoValidacionNif { Nif = nif, Nombre = nombre };

            if (ClientesSimplificadas.Contains(ficha.Nº_Cliente?.Trim()))
            {
                resultado.Estado = EstadoValidacionNif.Excluido;
                return resultado;
            }

            ValidacionNifRegistro registro = await almacen
                .Leer(ficha.Empresa?.Trim(), ficha.Nº_Cliente?.Trim(), ficha.Contacto?.Trim())
                .ConfigureAwait(false);

            // Sin registro, o la ficha cambió de NIF/nombre después de validar → sin validar.
            if (registro == null || registro.Nif?.Trim() != nif || registro.Nombre?.Trim() != nombre)
            {
                resultado.Estado = EstadoValidacionNif.SinValidar;
                return resultado;
            }

            resultado.Estado = registro.Estado == ESTADO_CORRECTO
                ? EstadoValidacionNif.Correcto
                : EstadoValidacionNif.Incorrecto;
            resultado.ResultadoAeat = registro.ResultadoAeat;
            return resultado;
        }
    }

    /// <summary>
    /// Acceso por SQL crudo a ValidacionesNif (tabla fuera del EDMX, patrón Cargos/EstadosCCC).
    /// </summary>
    public class AlmacenValidacionesNif : IAlmacenValidacionesNif
    {
        private readonly NVEntities db;

        public AlmacenValidacionesNif(NVEntities db)
        {
            this.db = db;
        }

        public async Task<ValidacionNifRegistro> Leer(string empresa, string cliente, string contacto)
        {
            List<ValidacionNifRegistro> filas = await db.Database.SqlQuery<ValidacionNifRegistro>(
                "SELECT Empresa, Cliente, Contacto, Nif, Nombre, Estado, ResultadoAeat, FechaValidacion, Usuario " +
                "FROM ValidacionesNif WHERE Empresa = @p0 AND Cliente = @p1 AND Contacto = @p2",
                empresa, cliente, contacto).ToListAsync().ConfigureAwait(false);
            ValidacionNifRegistro registro = filas.FirstOrDefault();
            if (registro != null)
            {
                registro.Empresa = registro.Empresa?.Trim();
                registro.Cliente = registro.Cliente?.Trim();
                registro.Contacto = registro.Contacto?.Trim();
                registro.Nif = registro.Nif?.Trim();
                registro.Nombre = registro.Nombre?.Trim();
                registro.Estado = registro.Estado?.Trim();
            }
            return registro;
        }

        public async Task Guardar(ValidacionNifRegistro registro)
        {
            _ = await db.Database.ExecuteSqlCommandAsync(
                "UPDATE ValidacionesNif SET Nif = @p3, Nombre = @p4, Estado = @p5, ResultadoAeat = @p6, " +
                "FechaValidacion = GETDATE(), Usuario = @p7 " +
                "WHERE Empresa = @p0 AND Cliente = @p1 AND Contacto = @p2; " +
                "IF @@ROWCOUNT = 0 " +
                "INSERT INTO ValidacionesNif (Empresa, Cliente, Contacto, Nif, Nombre, Estado, ResultadoAeat, Usuario) " +
                "VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7)",
                new SqlParameter("@p0", registro.Empresa),
                new SqlParameter("@p1", registro.Cliente),
                new SqlParameter("@p2", registro.Contacto),
                new SqlParameter("@p3", registro.Nif),
                new SqlParameter("@p4", registro.Nombre),
                new SqlParameter("@p5", registro.Estado),
                new SqlParameter("@p6", (object)registro.ResultadoAeat ?? DBNull.Value),
                new SqlParameter("@p7", (object)registro.Usuario ?? DBNull.Value))
                .ConfigureAwait(false);
        }
    }
}
