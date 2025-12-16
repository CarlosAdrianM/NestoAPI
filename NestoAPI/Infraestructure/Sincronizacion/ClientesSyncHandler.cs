using NestoAPI.Models;
using NestoAPI.Models.Sincronizacion;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
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

        public string TableName => "Clientes";

        public ClientesSyncHandler()
        {
            _changeDetector = new ClienteChangeDetector();
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

                    if (!cambios.Any())
                    {
                        Console.WriteLine($"⚪ Cliente {clienteExterno}-{contactoExterno}: Sin cambios en datos principales, NO SE ACTUALIZA");

                        // Continuar procesando PersonasContacto aunque el cliente no haya cambiado
                        if (message.PersonasContacto != null && message.PersonasContacto.Any())
                        {
                            Console.WriteLine($"   ℹ️ Procesando {message.PersonasContacto.Count} PersonasContacto...");
                            await ProcesarPersonasContacto(clienteExterno, contactoExterno, message.PersonasContacto);
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

                    // Actualizar el cliente (campos básicos)
                    ActualizarClienteDesdeExterno(clienteNesto, message);

                    // Actualizar vendedor si viene en el mensaje y es válido
                    await ActualizarVendedorSiValido(db, clienteNesto, message, clienteExterno, contactoExterno);

                    _ = await db.SaveChangesAsync();

                    Console.WriteLine($"✅ Cliente {clienteExterno}-{contactoExterno} actualizado exitosamente");

                    // Procesar personas de contacto si existen
                    if (message.PersonasContacto != null && message.PersonasContacto.Any())
                    {
                        Console.WriteLine($"   ℹ️ Procesando {message.PersonasContacto.Count} PersonasContacto...");
                        await ProcesarPersonasContacto(clienteExterno, contactoExterno, message.PersonasContacto);
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
        /// Actualiza el vendedor del cliente si viene en el mensaje y el vendedor existe en la BD.
        /// NOTA: El vendedor ya ha sido resuelto previamente por ResolverVendedorPorEmailSiNecesario.
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
                // No hay vendedor que actualizar
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

        private void ActualizarClienteDesdeExterno(Cliente clienteNesto, ClienteSyncMessage clienteExterno)
        {
            if (!string.IsNullOrWhiteSpace(clienteExterno.Nombre))
            {
                clienteNesto.Nombre = clienteExterno.Nombre;
            }

            if (!string.IsNullOrWhiteSpace(clienteExterno.Telefono))
            {
                clienteNesto.Teléfono = clienteExterno.Telefono;
            }

            if (!string.IsNullOrWhiteSpace(clienteExterno.Direccion))
            {
                clienteNesto.Dirección = clienteExterno.Direccion;
            }

            if (!string.IsNullOrWhiteSpace(clienteExterno.Poblacion))
            {
                clienteNesto.Población = clienteExterno.Poblacion;
            }

            if (!string.IsNullOrWhiteSpace(clienteExterno.CodigoPostal))
            {
                clienteNesto.CodPostal = clienteExterno.CodigoPostal;
            }

            if (!string.IsNullOrWhiteSpace(clienteExterno.Provincia))
            {
                clienteNesto.Provincia = clienteExterno.Provincia;
            }

            if (!string.IsNullOrWhiteSpace(clienteExterno.Nif))
            {
                clienteNesto.CIF_NIF = clienteExterno.Nif;
            }

            if (!string.IsNullOrWhiteSpace(clienteExterno.Comentarios))
            {
                clienteNesto.Comentarios = clienteExterno.Comentarios;
            }

            clienteNesto.Fecha_Modificación = DateTime.Now;
            clienteNesto.Usuario = "EXTERNAL_SYNC";
        }

        private async Task ProcesarPersonasContacto(
            string clienteExterno,
            string contactoExterno,
            List<PersonaContactoSyncDTO> personasExternas)
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

                    ActualizarPersonaContactoDesdeExterno(personaNesto, personaExterna);
                    _ = await db.SaveChangesAsync();

                    Console.WriteLine($"      ✅ {clienteExterno}-{contactoExterno}-{personaContactoExterna}: Actualizada exitosamente");
                }
            }
        }

        private void ActualizarPersonaContactoDesdeExterno(
            PersonaContactoCliente personaNesto,
            PersonaContactoSyncDTO personaExterna)
        {
            if (!string.IsNullOrWhiteSpace(personaExterna.Nombre))
            {
                personaNesto.Nombre = personaExterna.Nombre;
            }

            if (!string.IsNullOrWhiteSpace(personaExterna.Telefonos))
            {
                personaNesto.Teléfono = personaExterna.Telefonos;
            }

            if (!string.IsNullOrWhiteSpace(personaExterna.CorreoElectronico))
            {
                personaNesto.CorreoElectrónico = personaExterna.CorreoElectronico;
            }

            personaNesto.Fecha_Modificación = DateTime.Now;
            personaNesto.Usuario = "EXTERNAL_SYNC";
        }
    }
}
