using NestoAPI.Models;
using NestoAPI.Models.Sincronizacion;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Sincronizacion
{
    /// <summary>
    /// Handler de sincronización para la tabla Clientes
    /// Procesa actualizaciones de clientes y personas de contacto desde sistemas externos
    /// </summary>
    public class ClientesSyncHandler : ISyncTableHandler<ClienteSyncMessage>
    {
        private readonly ClienteChangeDetector _changeDetector;
        private readonly Action<Exception> _trazarDescartado;

        /// <summary>11/11/26: usuario con el que queda en ELMAH lo que descarta la barrera (el handler corre sin petición HTTP).</summary>
        public const string USUARIO_TRAZA = "Sistema (sincronización de clientes)";

        public string TableName => "Clientes";

        public ClientesSyncHandler() : this(ex => ElmahHelper.Log(ex, USUARIO_TRAZA))
        {
        }

        /// <summary>Con la traza de lo que descarta <see cref="BarreraClientesExternos"/> inyectable (tests).</summary>
        internal ClientesSyncHandler(Action<Exception> trazarDescartado)
        {
            _changeDetector = new ClienteChangeDetector();
            _trazarDescartado = trazarDescartado ?? (_ => { });
        }

        /// <summary>Una traza informativa por cada campo descartado (sin datos personales: cliente, campo y motivo).</summary>
        internal void TrazarDescartados(IEnumerable<CampoDescartadoSincronizacion> descartados)
        {
            foreach (CampoDescartadoSincronizacion descartado in descartados ?? Enumerable.Empty<CampoDescartadoSincronizacion>())
            {
                Console.WriteLine($"   🛡️ {descartado}");
                try
                {
                    _trazarDescartado(new CampoDescartadoSincronizacionException(descartado));
                }
                catch
                {
                    // La traza nunca tumba la sincronización.
                }
            }
        }

        // Implementación base polimórfica
        Task<bool> ISyncTableHandlerBase.HandleAsync(SyncMessageBase message)
        {
            return HandleAsync(message as ClienteSyncMessage);
        }

        string ISyncTableHandlerBase.GetMessageKey(SyncMessageBase message)
        {
            return GetMessageKey(message as ClienteSyncMessage);
        }

        string ISyncTableHandlerBase.GetLogInfo(SyncMessageBase message)
        {
            return GetLogInfo(message as ClienteSyncMessage);
        }

        public SyncMessageBase Deserialize(string json, JsonSerializerOptions options)
        {
            return JsonSerializer.Deserialize<ClienteSyncMessage>(json, options);
        }

        // Implementación tipada
        public string GetMessageKey(ClienteSyncMessage message)
        {
            var cliente = message?.Cliente?.Trim() ?? "NULL";
            var contacto = message?.Contacto?.Trim() ?? "NULL";
            var source = message?.Source?.Trim() ?? "NULL";
            return $"CLIENTE|{cliente}|{contacto}|{source}";
        }

        public string GetLogInfo(ClienteSyncMessage message)
        {
            var info = $"Cliente {message?.Cliente?.Trim() ?? "NULL"}";

            if (!string.IsNullOrEmpty(message?.Contacto))
            {
                info += $", Contacto {message.Contacto.Trim()}";
            }

            if (!string.IsNullOrEmpty(message?.Source))
            {
                info += $", Source={message.Source}";
            }

            if (message?.PersonasContacto != null && message.PersonasContacto.Count > 0)
            {
                var personasInfo = string.Join(", ", message.PersonasContacto.Select(p =>
                    $"Id={p.Id} ({p.Nombre})"
                ));
                info += $", PersonasContacto=[{personasInfo}]";
            }

            return info;
        }

        public async Task<bool> HandleAsync(ClienteSyncMessage message)
        {
            try
            {
                if (message == null)
                {
                    Console.WriteLine("⚠️ Mensaje nulo, omitiendo");
                    return false;
                }

                var clienteExterno = message.Cliente?.Trim();
                var contactoExterno = message.Contacto?.Trim();

                if (string.IsNullOrEmpty(clienteExterno) || string.IsNullOrEmpty(contactoExterno))
                {
                    Console.WriteLine($"⚠️ Cliente o Contacto vacío: Cliente={clienteExterno}, Contacto={contactoExterno}");
                    return false;
                }

                // Log con información completa del cliente
                string personasInfo = message.PersonasContacto != null && message.PersonasContacto.Any()
                    ? $", PersonasContacto=[{string.Join(", ", message.PersonasContacto.Select(p => p.Id))}]"
                    : "";

                Console.WriteLine($"🔍 Procesando Cliente {clienteExterno}-{contactoExterno}{personasInfo} (Source={message.Source})");

                using (var db = new NVEntities())
                {
                    // Buscar el cliente en Nesto
                    var clienteNesto = await db.Clientes
                        .Where(c => c.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO
                                && c.Nº_Cliente.Trim() == clienteExterno
                                && c.Contacto.Trim() == contactoExterno)
                        .FirstOrDefaultAsync();

                    // Resolver el vendedor por email si no viene el código
                    // Esto permite que DetectarCambios detecte cambios de vendedor por email
                    await ResolverVendedorPorEmailSiNecesario(db, message, clienteExterno, contactoExterno);

                    // Detectar cambios
                    var cambios = _changeDetector.DetectarCambios(clienteNesto, message);

                    // 11/11/26 (eco de Odoo): de lo distinto, solo lo que pasa la barrera; si no queda nada, es como si no hubiera cambios.
                    HashSet<string> campos = new HashSet<string>();
                    bool cambiaVendedor = cambios.Any(c => c.StartsWith("Vendedor:", StringComparison.Ordinal));
                    if (cambios.Any() && clienteNesto != null)
                    {
                        var descartados = new List<CampoDescartadoSincronizacion>();
                        campos = BarreraClientesExternos.CamposAAplicar(clienteNesto, message, descartados);
                        TrazarDescartados(descartados);
                        if (campos.Count == 0 && !cambiaVendedor)
                        {
                            cambios = new List<string>();
                        }
                    }

                    if (!cambios.Any())
                    {
                        Console.WriteLine($"⚪ Cliente {clienteExterno}-{contactoExterno}: Sin cambios en datos principales");

                        // Aunque no haya cambios en datos principales, verificar si hay que actualizar vendedor a NV
                        // (caso: vendedor eliminado en Odoo)
                        bool vendedorActualizado = await ActualizarVendedorEliminadoEnOdoo(db, clienteNesto, message, clienteExterno, contactoExterno);
                        if (vendedorActualizado)
                        {
                            _ = await db.SaveChangesAsync();
                        }

                        // Continuar procesando PersonasContacto aunque el cliente no haya cambiado
                        if (message.PersonasContacto != null && message.PersonasContacto.Any())
                        {
                            Console.WriteLine($"   ℹ️ Procesando {message.PersonasContacto.Count} PersonasContacto...");
                            await ProcesarPersonasContacto(clienteExterno, contactoExterno, message.PersonasContacto, message.Source);
                        }

                        return true;
                    }

                    Console.WriteLine($"🔄 Cliente {clienteExterno}-{contactoExterno}: Cambios detectados:");
                    foreach (var cambio in cambios)
                    {
                        Console.WriteLine($"   - {cambio}");
                    }

                    if (clienteNesto == null)
                    {
                        Console.WriteLine($"⚠️ Cliente {clienteExterno}-{contactoExterno} no existe en Nesto. No se puede crear desde sistemas externos.");
                        return false;
                    }

                    // Actualizar el cliente: solo los campos distintos que pasan la barrera
                    if (!BarreraClientesExternos.Aplicar(clienteNesto, message, campos))
                    {
                        clienteNesto.Fecha_Modificación = DateTime.Now;
                        clienteNesto.Usuario = "EXTERNAL_SYNC";
                    }

                    // Actualizar vendedor si viene en el mensaje y es válido
                    await ActualizarVendedorSiValido(db, clienteNesto, message, clienteExterno, contactoExterno);

                    _ = await db.SaveChangesAsync();

                    Console.WriteLine($"✅ Cliente {clienteExterno}-{contactoExterno} actualizado exitosamente");

                    // Procesar personas de contacto si existen
                    if (message.PersonasContacto != null && message.PersonasContacto.Any())
                    {
                        Console.WriteLine($"   ℹ️ Procesando {message.PersonasContacto.Count} PersonasContacto...");
                        await ProcesarPersonasContacto(clienteExterno, contactoExterno, message.PersonasContacto, message.Source);
                    }

                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error procesando cliente: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                return false;
            }
        }

        /// <summary>
        /// Resuelve el código de vendedor desde el email si no viene el código directo.
        /// Modifica el mensaje para que tenga el código de vendedor resuelto.
        /// </summary>
        private async Task ResolverVendedorPorEmailSiNecesario(
            NVEntities db,
            ClienteSyncMessage message,
            string clienteExterno,
            string contactoExterno)
        {
            // Si ya viene el código de vendedor, no hay nada que hacer
            if (!string.IsNullOrWhiteSpace(message.Vendedor))
            {
                return;
            }

            // Si no viene código pero sí viene email, buscar el vendedor por email
            if (!string.IsNullOrWhiteSpace(message.VendedorEmail))
            {
                var emailBuscar = message.VendedorEmail.Trim();
                var vendedor = await db.Vendedores
                    .FirstOrDefaultAsync(v => v.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO
                                           && v.Mail != null
                                           && v.Mail.Trim() == emailBuscar);

                if (vendedor != null)
                {
                    Console.WriteLine($"   ℹ️ Vendedor resuelto por email '{emailBuscar}' → '{vendedor.Número?.Trim()}'");
                    // Modificar el mensaje para que tenga el código de vendedor resuelto
                    message.Vendedor = vendedor.Número?.Trim();
                }
                else
                {
                    Console.WriteLine($"   ⚠️ No se encontró vendedor con email '{emailBuscar}' para cliente {clienteExterno}-{contactoExterno}");
                }
            }
        }

        /// <summary>
        /// Verifica si el vendedor fue eliminado en Odoo y lo actualiza a NV.
        /// Se usa cuando no hay otros cambios detectados pero el vendedor puede haber sido eliminado.
        /// </summary>
        /// <returns>True si se actualizó el vendedor, False si no hubo cambios</returns>
        private Task<bool> ActualizarVendedorEliminadoEnOdoo(
            NVEntities db,
            Cliente clienteNesto,
            ClienteSyncMessage message,
            string clienteExterno,
            string contactoExterno)
        {
            // Solo aplica cuando VendedorEmail viene como cadena vacía (no null) desde cualquier sistema externo
            // VendedorEmail = null significa "no modificar", VendedorEmail = "" significa "vendedor eliminado"
            if (message.VendedorEmail == "" && string.IsNullOrWhiteSpace(message.Vendedor))
            {
                // Solo actualizar si el cliente tiene un vendedor diferente a NV
                if (clienteNesto?.Vendedor?.Trim() != Constantes.Vendedores.VENDEDOR_GENERAL)
                {
                    clienteNesto.Vendedor = Constantes.Vendedores.VENDEDOR_GENERAL;
                    clienteNesto.Fecha_Modificación = DateTime.Now;
                    clienteNesto.Usuario = "EXTERNAL_SYNC";
                    Console.WriteLine($"   ✅ Vendedor eliminado en Odoo para {clienteExterno}-{contactoExterno}, asignando '{Constantes.Vendedores.VENDEDOR_GENERAL}'");
                    return Task.FromResult(true);
                }
            }
            return Task.FromResult(false);
        }

        /// <summary>
        /// Actualiza el vendedor del cliente si viene en el mensaje y el vendedor existe en la BD.
        /// NOTA: El vendedor ya ha sido resuelto previamente por ResolverVendedorPorEmailSiNecesario.
        /// Si el vendedor viene vacío desde Odoo (VendedorEmail = ""), asigna el vendedor por defecto "NV".
        /// </summary>
        private async Task ActualizarVendedorSiValido(
            NVEntities db,
            Cliente clienteNesto,
            ClienteSyncMessage message,
            string clienteExterno,
            string contactoExterno)
        {
            // El vendedor ya fue resuelto previamente (por código o por email)
            if (string.IsNullOrWhiteSpace(message.Vendedor))
            {
                // Si VendedorEmail viene como cadena vacía (no null) desde cualquier sistema,
                // significa que se eliminó el vendedor → asignar NV
                if (message.VendedorEmail == "")
                {
                    clienteNesto.Vendedor = Constantes.Vendedores.VENDEDOR_GENERAL;
                    Console.WriteLine($"   ✅ Vendedor eliminado en sistema externo, asignando '{Constantes.Vendedores.VENDEDOR_GENERAL}'");
                }
                return;
            }

            var vendedorCodigo = message.Vendedor.Trim();

            // Verificar que el vendedor existe en la tabla Vendedores
            var vendedorExiste = await db.Vendedores
                .AnyAsync(v => v.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO
                            && v.Número == vendedorCodigo);

            if (!vendedorExiste)
            {
                Console.WriteLine($"   ⚠️ Vendedor '{vendedorCodigo}' no existe en Nesto. No se actualiza el vendedor del cliente {clienteExterno}-{contactoExterno}");
                return;
            }

            // Actualizar el vendedor
            clienteNesto.Vendedor = vendedorCodigo;
            Console.WriteLine($"   ✅ Vendedor actualizado a '{vendedorCodigo}'");
        }

        private async Task ProcesarPersonasContacto(
            string clienteExterno,
            string contactoExterno,
            List<PersonaContactoSyncDTO> personasExternas,
            string source)
        {
            using (var db = new NVEntities())
            {
                foreach (var personaExterna in personasExternas)
                {
                    var personaContactoExterna = personaExterna.Id?.Trim();

                    if (string.IsNullOrEmpty(personaContactoExterna))
                    {
                        Console.WriteLine($"      ⚠️ PersonaContacto con Id vacío, omitiendo");
                        continue;
                    }

                    Console.WriteLine($"      🔍 PersonaContacto {clienteExterno}-{contactoExterno}-{personaContactoExterna} ({personaExterna.Nombre})");

                    var personaNesto = await db.PersonasContactoClientes
                        .Where(p => p.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO
                                && p.NºCliente.Trim() == clienteExterno
                                && p.Contacto.Trim() == contactoExterno
                                && p.Número.Trim() == personaContactoExterna)
                        .FirstOrDefaultAsync();

                    var cambios = _changeDetector.DetectarCambiosPersonaContacto(personaNesto, personaExterna);

                    if (!cambios.Any())
                    {
                        Console.WriteLine($"      ⚪ {clienteExterno}-{contactoExterno}-{personaContactoExterna}: Sin cambios, NO SE ACTUALIZA");
                        continue;
                    }

                    Console.WriteLine($"      🔄 {clienteExterno}-{contactoExterno}-{personaContactoExterna}: Cambios detectados:");
                    foreach (var cambio in cambios)
                    {
                        Console.WriteLine($"         - {cambio}");
                    }

                    if (personaNesto == null)
                    {
                        Console.WriteLine($"      ⚠️ {clienteExterno}-{contactoExterno}-{personaContactoExterna}: No existe en Nesto");
                        continue;
                    }

                    var descartados = new List<CampoDescartadoSincronizacion>();
                    HashSet<string> campos = BarreraClientesExternos.CamposAAplicarPersona(personaNesto, personaExterna, source,
                        clienteExterno, contactoExterno, descartados);
                    TrazarDescartados(descartados);
                    if (!BarreraClientesExternos.AplicarPersona(personaNesto, personaExterna, campos))
                    {
                        Console.WriteLine($"      ⚪ {clienteExterno}-{contactoExterno}-{personaContactoExterna}: lo distinto no pasa la barrera, NO SE ACTUALIZA");
                        continue;
                    }
                    _ = await db.SaveChangesAsync();

                    Console.WriteLine($"      ✅ {clienteExterno}-{contactoExterno}-{personaContactoExterna}: Actualizada exitosamente");
                }
            }
        }
    }
}
