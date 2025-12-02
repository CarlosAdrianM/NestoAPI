using NestoAPI.Models;
using NestoAPI.Models.ApuntesBanco;
using NestoAPI.Models.Bancos;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Contabilidad
{
    public class ContabilidadService : IContabilidadService
    {
        public async Task<int> ContabilizarDiario(string empresa, string diario, string usuario)
        {
            using (NVEntities db = new NVEntities())
            {
                return await ContabilizarDiario(db, empresa, diario, usuario);
            }
        }

        public async Task<int> ContabilizarDiario(NVEntities db, string empresa, string diario, string usuario)
        {
            SqlParameter empresaParametro = new SqlParameter("@Empresa", SqlDbType.Char, 3)
            {
                Value = empresa
            };

            SqlParameter diarioParametro = new SqlParameter("@Diario", SqlDbType.Char, 10)
            {
                Value = diario
            };
            SqlParameter usuarioParametro = new SqlParameter("@Usuario", SqlDbType.Char, 30)
            {
                Value = usuario
            };
            //var resultadoProcedimiento = await db.Database.ExecuteSqlCommandAsync("EXEC prdContabilizar @Empresa, @Diario", empresaParametro, diarioParametro);
            //return resultadoProcedimiento;
            SqlParameter resultadoParametro = new SqlParameter
            {
                ParameterName = "@Resultado",
                SqlDbType = SqlDbType.Int,
                Direction = ParameterDirection.Output // Configurar para capturar el valor de retorno
            };
            try
            {
                // Ejecutar el procedimiento almacenado y capturar el valor de retorno
                int resultadoDirecto = await db.Database.ExecuteSqlCommandAsync("EXEC @Resultado = prdContabilizar @Empresa, @Diario, @Usuario", resultadoParametro, empresaParametro, diarioParametro, usuarioParametro);
                // Obtener el valor de retorno del parámetro
                int resultadoProcedimiento = (int)resultadoParametro.Value;
                return resultadoProcedimiento;
            }
            catch (Exception ex)
            {
                throw new Exception("Error al contabilizar el diario", ex);
            }
        }

        public async Task<int> CrearLineas(NVEntities db, List<PreContabilidad> lineas)
        {
            foreach (PreContabilidad linea in lineas)
            {
                if (linea.FechaVto == null || linea.FechaVto == DateTime.MinValue)
                {
                    linea.FechaVto = new DateTime(linea.Fecha.Year, linea.Fecha.Month, linea.Fecha.Day);
                }
                linea.Fecha_Modificación = DateTime.Now;
                if (linea.Concepto != null && linea.Concepto.Length > 50)
                {
                    linea.Concepto = linea.Concepto.Substring(0, 50);
                }
                _ = db.PreContabilidades.Add(linea);

                // Ejecutar prdCopiarCliente si corresponde
                if (linea.Empresa != Constantes.Empresas.EMPRESA_POR_DEFECTO &&
                    linea.TipoCuenta == Constantes.Contabilidad.TiposCuenta.CLIENTE)
                {
                    SqlParameter empresaOrigenParam = new SqlParameter("@EmpresaOrigen", SqlDbType.Char, 3)
                    {
                        Value = Constantes.Empresas.EMPRESA_POR_DEFECTO
                    };

                    SqlParameter empresaDestinoParam = new SqlParameter("@EmpresaDestino", SqlDbType.Char, 3)
                    {
                        Value = linea.Empresa
                    };

                    SqlParameter numClienteParam = new SqlParameter("@NumCliente", SqlDbType.Char, 10)
                    {
                        Value = linea.Nº_Cuenta
                    };

                    _ = await db.Database.ExecuteSqlCommandAsync(
                        "EXEC prdCopiarCliente @EmpresaOrigen, @EmpresaDestino, @NumCliente",
                        empresaOrigenParam, empresaDestinoParam, numClienteParam);
                }
            }
            try
            {
                _ = await db.SaveChangesAsync().ConfigureAwait(false);
                return lineas.Last().Nº_Orden;
            }
            catch (Exception ex)
            {
                throw new Exception("No se ha podido contabilizar el diario", ex);
            }

        }

        public async Task<int> CrearLineas(List<PreContabilidad> lineas)
        {
            using (NVEntities db = new NVEntities())
            {
                return await CrearLineas(db, lineas);
            }
        }

        public async Task<int> CrearLineasYContabilizarDiario(List<PreContabilidad> lineas)
        {
            using (NVEntities db = new NVEntities())
            {
                using (DbContextTransaction transaction = db.Database.BeginTransaction())
                {
                    try
                    {
                        int resultado = await CrearLineasYContabilizarDiario(lineas, db);
                        if (resultado > 0)
                        {
                            transaction.Commit();
                        }
                        else
                        {
                            transaction.Rollback();
                        }
                        return resultado;
                    }
                    catch (Exception ex)
                    {
                        // Carlos 02/12/25: Verificar estado de conexión antes de rollback (Issue #47)
                        // El rollback puede fallar si la conexión ya está cerrada (timeout, error de red, etc.)
                        try
                        {
                            if (db.Database.Connection.State == ConnectionState.Open)
                            {
                                transaction.Rollback();
                            }
                        }
                        catch
                        {
                            // Ignorar errores en rollback - la transacción se revertirá automáticamente
                            // cuando se cierre la conexión o expire el timeout
                        }
                        throw new Exception("Error al contabilizar el diario. Inténtelo de nuevo.", ex);
                    }
                }
            }
        }

        public async Task<int> CrearLineasYContabilizarDiario(List<PreContabilidad> lineas, NVEntities db)
        {
            // Ojo, aquí hay que entrar ya en transacción y hacer try/catch con el rollback y el commit desde el método que llama            
            _ = await CrearLineas(db, lineas);
            int resultado = await ContabilizarDiario(db, lineas[0].Empresa, lineas[0].Diario, lineas[0].Usuario);
            return resultado;
        }

        public async Task<bool> PersistirCuaderno43(ContenidoCuaderno43 contenido)
        {
            using (NVEntities db = new NVEntities())
            {
                try
                {
                    FicheroCuaderno43 fichero = new FicheroCuaderno43
                    {
                        // Si en algún momento tenemos dos cuentas en el mismo fichero, lo trataríamos como dos ficheros
                        // Asignar valores de la clase RegistroCabeceraCuenta a la instancia de FicheroCuaderno43
                        ClaveEntidad = contenido.Cabecera.ClaveEntidad,
                        ClaveOficina = contenido.Cabecera.ClaveOficina,
                        NumeroCuenta = contenido.Cabecera.NumeroCuenta,
                        FechaInicial = contenido.Cabecera.FechaInicial,
                        FechaFinal = contenido.Cabecera.FechaFinal,
                        ClaveDebeOHaber = contenido.Cabecera.ClaveDebeOHaber,
                        ImporteSaldoInicial = contenido.Cabecera.ImporteSaldoInicial,
                        ClaveDivisa = contenido.Cabecera.ClaveDivisa,
                        ModalidadInformacion = contenido.Cabecera.ModalidadInformacion,
                        NombreAbreviado = contenido.Cabecera.NombreAbreviado,

                        // Asignar valores de la clase RegistroFinalCuenta a la instancia de FicheroCuaderno43
                        // Esto está mal en fichero son re
                        TotalImportesDebe = contenido.FinalCuenta.TotalImportesDebe,
                        NumeroApuntesHaber = contenido.FinalCuenta.NumeroApuntesHaber,
                        TotalImportesHaber = contenido.FinalCuenta.TotalImportesHaber,
                        CodigoSaldoFinal = contenido.FinalCuenta.CodigoSaldoFinal,
                        SaldoFinal = contenido.FinalCuenta.SaldoFinal,

                        // Asignar valores adicionales o predeterminados
                        NumeroRegistros = contenido.FinalFichero.NumeroRegistros,
                        Usuario = contenido.Usuario,
                        FechaCreacion = DateTime.Now // Fecha actual                    
                    };

                    Banco banco = await db.Bancos.SingleAsync(b => b.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && b.Entidad == fichero.ClaveEntidad && b.Sucursal == fichero.ClaveOficina && b.Nº_Cuenta == fichero.NumeroCuenta);
                    DateTime fechaFichero = contenido.Apuntes.First().FechaOperacion;
                    if (db.ApuntesBancarios.Any(c => c.FechaOperacion == fechaFichero && c.Empresa == banco.Empresa && c.BancoId == banco.Número))
                    {
                        throw new Exception($"Ya se ha contabilizado el fichero del día {fechaFichero.ToShortDateString()}");
                    }
                    // Iterar sobre cada ApunteBancarioDTO en la lista contenido.Apuntes
                    foreach (ApunteBancarioDTO apunteDto in contenido.Apuntes)
                    {
                        // Crear una nueva instancia de ApunteBancario
                        ApunteBancario apunteBancario = new ApunteBancario
                        {
                            // Asignar valores de ApunteBancarioDTO a ApunteBancario
                            Empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO,
                            BancoId = banco.Número,
                            ClaveOficinaOrigen = apunteDto.ClaveOficinaOrigen,
                            FechaOperacion = apunteDto.FechaOperacion,
                            FechaValor = apunteDto.FechaValor,
                            ConceptoComun = apunteDto.ConceptoComun,
                            ConceptoPropio = apunteDto.ConceptoPropio,
                            ClaveDebeOHaberMovimiento = apunteDto.ClaveDebeOHaberMovimiento,
                            ImporteMovimiento = apunteDto.ImporteMovimiento,
                            NumeroDocumento = apunteDto.NumeroDocumento,
                            Referencia1 = apunteDto.Referencia1,
                            Referencia2 = apunteDto.Referencia2,
                            Usuario = contenido.Usuario,
                            FechaCreacion = DateTime.Now
                        };

                        // Iterar sobre cada ConceptoComplementario en apunteDto.RegistrosConcepto
                        foreach (ConceptoComplementario concepto in apunteDto.RegistrosConcepto)
                        {
                            // Crear una nueva instancia de RegistroComplementarioConcepto
                            RegistroComplementarioConcepto registroConcepto = new RegistroComplementarioConcepto
                            {
                                // Asignar valores de ConceptoComplementario a RegistroComplementarioConcepto
                                CodigoDato = concepto.CodigoDatoConcepto,
                                Concepto = concepto.Concepto,
                                Concepto2 = concepto.Concepto2,
                                Usuario = contenido.Usuario,
                                FechaCreacion = DateTime.Now
                            };


                            // Agregar RegistroComplementarioConcepto a la colección de apunteBancario
                            apunteBancario.RegistroComplementarioConceptoes.Add(registroConcepto);
                        }

                        // Si hay un objeto ImporteEquivalencia en apunteDto
                        if (apunteDto.ImporteEquivalencia != null && apunteDto.ImporteEquivalencia.ImporteEquivalencia != 0)
                        {
                            // Crear una nueva instancia de RegistroComplementarioEquivalencia
                            RegistroComplementarioEquivalencia registroEquivalencia = new RegistroComplementarioEquivalencia
                            {
                                // Asignar valores de EquivalenciaDivisas a RegistroComplementarioEquivalencia
                                CodigoDato = apunteDto.ImporteEquivalencia.CodigoDatoEquivalencia,
                                ClaveDivisaOrigen = apunteDto.ImporteEquivalencia.ClaveDivisaOrigen,
                                ImporteEquivalencia = apunteDto.ImporteEquivalencia.ImporteEquivalencia,
                                Usuario = contenido.Usuario,
                                FechaCreacion = DateTime.Now
                            };

                            // Agregar RegistroComplementarioEquivalencia a la colección de apunteBancario
                            apunteBancario.RegistroComplementarioEquivalencias.Add(registroEquivalencia);
                        }

                        // Agregar apunteBancario a la colección de apuntes bancarios de fichero
                        fichero.ApuntesBancarios.Add(apunteBancario);
                    }

                    // Agregar la instancia de FicheroCuaderno43 al contexto y guardar los cambios
                    _ = db.FicherosCuaderno43.Add(fichero);

                    _ = await db.SaveChangesAsync();
                    return true;
                }
                catch (Exception ex)
                {
                    throw ex;
                }
            }
        }

        public async Task<bool> PersistirMovimientosTPV(List<MovimientoTPVDTO> movimientosTPV)
        {
            using (NVEntities db = new NVEntities())
            {
                DateTime fechaFichero = movimientosTPV.First().FechaOperacion;
                if (db.MovimientosTPV.Any(c => c.FechaCaptura == fechaFichero))
                {
                    throw new Exception($"Ya se ha contabilizado el fichero de tarjetas del día {fechaFichero.ToShortDateString()}");
                }

                try
                {
                    foreach (MovimientoTPVDTO movimiento in movimientosTPV)
                    {
                        MovimientoTPV nuevoMovimiento = new MovimientoTPV
                        {
                            ModoCaptura = movimiento.ModoCaptura,
                            Sesion = movimiento.Sesion,
                            Terminal = movimiento.Terminal,
                            FechaCaptura = movimiento.FechaCaptura,
                            FechaOperacion = movimiento.FechaOperacion,
                            ImporteOperacion = movimiento.ImporteOperacion,
                            ImporteComision = movimiento.ImporteComision,
                            ImporteAbono = movimiento.ImporteAbono,
                            CodigoMoneda = movimiento.CodigoMoneda,
                            Comentarios = movimiento.Comentarios,
                            Usuario = movimiento.Usuario,
                            FechaCreacion = DateTime.Now
                        };
                        _ = db.MovimientosTPV.Add(nuevoMovimiento);
                    }
                    _ = await db.SaveChangesAsync();
                    return true;
                }
                catch (Exception ex)
                {
                    throw new Exception("No se ha podido persistir el fichero de tarjetas", ex);
                }

            }
        }

        public async Task<decimal> SaldoFinal(string entidad, string oficina, string cuenta, DateTime fecha)
        {
            using (NVEntities db = new NVEntities())
            {
                FicheroCuaderno43 contenido = await db.FicherosCuaderno43
                    .Where(c => c.ClaveEntidad == entidad && c.ClaveOficina == oficina && c.NumeroCuenta == cuenta && c.FechaFinal <= fecha)
                    .OrderByDescending(c => c.FechaFinal)
                    .FirstOrDefaultAsync();

                return contenido != null ? contenido.SaldoFinal : 0m;
            }
        }

        public async Task<decimal> SaldoInicial(string entidad, string oficina, string cuenta, DateTime fecha)
        {
            using (NVEntities db = new NVEntities())
            {
                FicheroCuaderno43 contenido = await db.FicherosCuaderno43
                    .Where(c =>
                        c.ClaveEntidad == entidad &&
                        c.ClaveOficina == oficina &&
                        c.NumeroCuenta == cuenta &&
                        c.FechaInicial <= fecha &&
                        c.FechaFinal >= fecha)
                    .OrderByDescending(c => c.FechaInicial) // por si hubiera solapamiento, cogemos el más reciente
                    .FirstOrDefaultAsync()
                    .ConfigureAwait(false);

                return contenido?.ImporteSaldoInicial ?? 0m;
            }
        }


        public async Task<List<MovimientoTPVDTO>> LeerMovimientosTPV(DateTime fechaCaptura, string tipoDatafono)
        {
            using (NVEntities db = new NVEntities())
            {
                try
                {
                    //List<MovimientoTPVDTO> movimientos = await db.MovimientosTPV
                    //    .Where(m => m.FechaCaptura == fechaCaptura && m.ModoCaptura == tipoDatafono)
                    //    .Select(m => new MovimientoTPVDTO
                    //    {
                    //        ModoCaptura = m.ModoCaptura,
                    //        Sesion = m.Sesion,
                    //        Terminal = m.Terminal,
                    //        FechaCaptura = m.FechaCaptura,
                    //        FechaOperacion = m.FechaOperacion,
                    //        ImporteOperacion = m.ImporteOperacion,
                    //        ImporteComision = m.ImporteComision,
                    //        ImporteAbono = m.ImporteAbono,
                    //        CodigoMoneda = m.CodigoMoneda,
                    //        Comentarios = m.Comentarios,
                    //        FechaCreacion = m.FechaCreacion,
                    //        Usuario = m.Usuario
                    //    })
                    //    .ToListAsync()
                    //    .ConfigureAwait(false);

                    var movimientosQuery = db.MovimientosTPV
                        .Where(m => m.FechaCaptura == fechaCaptura)
                        .Where(m =>
                            tipoDatafono == "3"
                                ? m.Terminal.EndsWith("32951570") && m.Terminal.Length >= 11
                                : !m.Terminal.EndsWith("32951570") || m.Terminal.Length < 11
                        );

                    var movimientos = await movimientosQuery
                        .Select(m => new MovimientoTPVDTO
                        {
                            ModoCaptura = m.ModoCaptura,
                            Sesion = m.Sesion,
                            Terminal = m.Terminal,
                            FechaCaptura = m.FechaCaptura,
                            FechaOperacion = m.FechaOperacion,
                            ImporteOperacion = m.ImporteOperacion,
                            ImporteComision = m.ImporteComision,
                            ImporteAbono = m.ImporteAbono,
                            CodigoMoneda = m.CodigoMoneda,
                            Comentarios = m.Comentarios,
                            FechaCreacion = m.FechaCreacion,
                            Usuario = m.Usuario
                        })
                        .ToListAsync()
                        .ConfigureAwait(false);

                    foreach (MovimientoTPVDTO movimiento in movimientos)
                    {
                        movimiento.TextoModoCaptura = MovimientoTPVDTO.GetModoCapturaDescription(movimiento.ModoCaptura);
                        movimiento.UsuarioTerminal = ObtenerUsuarioTerminal(movimiento.Terminal);
                    }

                    return movimientos;
                }
                catch (Exception ex)
                {
                    throw new Exception("No se han podido leer los movimientos de los TPV", ex);
                }

            }
        }
        private readonly Dictionary<string, string> TerminalesUsuarios = new Dictionary<string, string>()
        {
            { "91900804273", "Paloma" },
            { "91900804275", "Victoria" },
            { "26617120788", "Laura Camacho" },
            { "00346609775", "Patricia" },
            { "00132951570", "Web" },
            { "00232951570", "Paygold" },
            { "00025537534", "Pilar" },
            { "51570001329", "Bizum tienda online" },
            { "51570002329", "Bizum Paygold" },
            { "00022126270", "Almacén" },
        };
        private string ObtenerUsuarioTerminal(string terminal)
        {
            if (TerminalesUsuarios.TryGetValue(terminal, out string usuario))
            {
                return usuario;
            }
            return string.Empty; // O un valor predeterminado si prefieres
        }

        public async Task ContabilizarComisionesTarjetas(List<MovimientoTPVDTO> movimientosTPV)
        {
            using (NVEntities db = new NVEntities())
            {
                try
                {
                    List<PreContabilidad> lineas = new List<PreContabilidad>();
                    decimal sumaComisiones = 0;
                    // Si algún día trabajamos con un banco que no sea Caixa tenemos que crear un campo en MovimientoTPVDTO que sea el banco
                    // o si no pasar el banco a este método como un parámetro desde el controlador
                    Banco banco = await db.Bancos.SingleAsync(b => b.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && b.Entidad == "2100");
                    DateTime fechaIngreso = movimientosTPV.First().FechaCaptura.AddDays(1);
                    int numeroCobros = 0;
                    foreach (MovimientoTPVDTO movimiento in movimientosTPV.Where(m => m.ImporteComision != 0))
                    {
                        sumaComisiones += movimiento.ImporteComision;
                        decimal porcentajeComision = movimiento.ImporteComision / movimiento.ImporteOperacion;
                        PreContabilidad nuevaLinea = new PreContabilidad
                        {
                            Empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO,
                            Diario = Constantes.DiariosContables.COMISIONES_BANCO,
                            Asiento = 1,
                            Fecha = fechaIngreso,
                            TipoCuenta = Constantes.Contabilidad.TiposCuenta.CUENTA_CONTABLE,
                            TipoApunte = Constantes.TiposExtractoCliente.SIN_ESPECIFICAR,
                            Nº_Cuenta = Constantes.Cuentas.COMISIONES_BANCO_COBRO_TPV,
                            Concepto = $"Comisiones TPV {banco.Descripción.Trim()} ({porcentajeComision:p})",
                            Nº_Documento = movimiento.Terminal.Length > 10 ? movimiento.Terminal.Substring(movimiento.Terminal.Length - 10) : movimiento.Terminal,
                            Debe = movimiento.ImporteComision,
                            Asiento_Automático = true,
                            CentroCoste = "CA",
                            Delegación = "ALG",
                            Departamento = "ADM",
                            Usuario = movimiento.Usuario,
                            Fecha_Modificación = DateTime.Now
                        };
                        lineas.Add(nuevaLinea);
                        numeroCobros++;
                    }
                    PreContabilidad contrapartida = new PreContabilidad
                    {
                        Empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO,
                        Diario = Constantes.DiariosContables.COMISIONES_BANCO,
                        Asiento = 1,
                        Fecha = fechaIngreso,
                        TipoCuenta = Constantes.Contabilidad.TiposCuenta.CUENTA_CONTABLE,
                        TipoApunte = Constantes.TiposExtractoCliente.SIN_ESPECIFICAR,
                        Nº_Cuenta = banco.Cuenta_Contable,
                        Concepto = $"Comisiones TPV {banco.Descripción.Trim()} ({numeroCobros} cobros)",
                        Nº_Documento = $"TPV_{fechaIngreso:ddMMyy}",
                        Haber = sumaComisiones,
                        Asiento_Automático = true,
                        Delegación = "ALG",
                        Usuario = movimientosTPV.First().Usuario,
                        Fecha_Modificación = DateTime.Now
                    };
                    lineas.Add(contrapartida);

                    _ = await CrearLineasYContabilizarDiario(lineas);
                }
                catch (Exception ex)
                {
                    throw new Exception("Error al contabilizar las comisiones de las tarjetas", ex);
                }
            }
        }

        public async Task<int> NumeroRecibosRemesa(int remesa)
        {
            using (NVEntities db = new NVEntities())
            {
                IQueryable<ExtractoCliente> recibos = db.ExtractosCliente.Where(e => e.TipoApunte == Constantes.TiposExtractoCliente.PAGO && e.Remesa == remesa);
                int numeroRecibos = await recibos.CountAsync();
                return numeroRecibos;
            }
        }


        public async Task<string> LeerProveedorPorNif(string nifProveedor)
        {
            using (NVEntities db = new NVEntities())
            {
                string proveedor = (await db.Proveedores.SingleOrDefaultAsync(p => p.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && p.CIF_NIF.Contains(nifProveedor)))?.Número;
                return string.IsNullOrEmpty(proveedor) ? string.Empty : proveedor.Trim();
            }
        }

        public async Task<string> LeerProveedorPorNombre(string nombreProveedor)
        {
            using (NVEntities db = new NVEntities())
            {
                // nombreProveedor = LimpiarNombreProveedor(nombreProveedor); // quitar SL, SA, etc...
                string proveedor = (await db.Proveedores.SingleOrDefaultAsync(p => p.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && p.Nombre.Contains(nombreProveedor)))?.Número;
                return string.IsNullOrEmpty(proveedor) ? string.Empty : proveedor.Trim();
            }
        }

        public async Task<ExtractoProveedorDTO> PagoPendienteUnico(string proveedor, decimal importe)
        {
            using (NVEntities db = new NVEntities())
            {
                ExtractoProveedorDTO pagoPendiente = await db.ExtractosProveedor
                    .AsNoTracking()
                    .Where(e =>
                        e.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO &&
                        e.Número == proveedor &&
                        e.ImportePdte == importe
                    )
                    .Select(e => new ExtractoProveedorDTO
                    {
                        Id = e.NºOrden,
                        Empresa = e.Empresa.Trim(),
                        Proveedor = e.Número.Trim(),
                        Contacto = e.Contacto.Trim(),
                        Documento = e.NºDocumento.Trim(),
                        DocumentoProveedor = e.NºDocumentoProv.Trim(),
                        Delegacion = e.Delegación.Trim(),
                        FormaVenta = e.FormaVenta.Trim()
                    })
                    .SingleOrDefaultAsync();

                return pagoPendiente;
            }
        }

        //public async Task<bool> PuntearPorImporte(string empresa, string cuenta, decimal importe)
        //{
        //    try
        //    {
        //        using (var db = new NVEntities())
        //        {
        //            var movimientoSinPuntearDebe = await db.Contabilidades
        //                .SingleAsync(c => c.Empresa == empresa && c.Nº_Cuenta == cuenta && c.Debe == importe && c.Punteado == false);
        //            var movimientoSinPuntearHaber = await db.Contabilidades
        //                .SingleAsync(c => c.Empresa == empresa && c.Nº_Cuenta == cuenta && c.Haber == importe && c.Punteado == false);
        //            movimientoSinPuntearDebe.Punteado = true;
        //            movimientoSinPuntearHaber.Punteado = true;
        //            int movimientosModificados = await db.SaveChangesAsync();
        //            return movimientosModificados == 2; // Comprobamos que se hayan punteado los dos
        //        }                
        //    }
        //    catch
        //    {
        //        return false;
        //    }
        //}

        public async Task<bool> PuntearPorImporte(string empresa, string cuenta, decimal importe)
        {
            try
            {
                using (NVEntities db = new NVEntities())
                {
                    using (DbContextTransaction transaction = db.Database.BeginTransaction())
                    {
                        try
                        {
                            Models.Contabilidad movimientoSinPuntearDebe = await db.Contabilidades
                                .SingleAsync(c => c.Empresa == empresa && c.Nº_Cuenta == cuenta && c.Debe == importe && c.Punteado == false);
                            Models.Contabilidad movimientoSinPuntearHaber = await db.Contabilidades
                                .SingleAsync(c => c.Empresa == empresa && c.Nº_Cuenta == cuenta && c.Haber == importe && c.Punteado == false);

                            // Crear el comando para el primer movimiento
                            System.Data.Common.DbCommand commandDebe = db.Database.Connection.CreateCommand();
                            commandDebe.CommandText = "EXEC [dbo].[prdPuntearContabilidad] @NºOrden, @Punteado";
                            _ = commandDebe.Parameters.Add(new SqlParameter("@NºOrden", movimientoSinPuntearDebe.Nº_Orden));
                            _ = commandDebe.Parameters.Add(new SqlParameter("@Punteado", true));

                            // Crear el comando para el segundo movimiento
                            System.Data.Common.DbCommand commandHaber = db.Database.Connection.CreateCommand();
                            commandHaber.CommandText = "EXEC [dbo].[prdPuntearContabilidad] @NºOrden, @Punteado";
                            _ = commandHaber.Parameters.Add(new SqlParameter("@NºOrden", movimientoSinPuntearHaber.Nº_Orden));
                            _ = commandHaber.Parameters.Add(new SqlParameter("@Punteado", true));

                            // Abrir la conexión si no está abierta
                            if (db.Database.Connection.State != ConnectionState.Open)
                            {
                                await db.Database.Connection.OpenAsync();
                            }

                            // Ejecutar los comandos dentro de la transacción
                            commandDebe.Transaction = transaction.UnderlyingTransaction;
                            _ = await commandDebe.ExecuteNonQueryAsync();

                            commandHaber.Transaction = transaction.UnderlyingTransaction;
                            _ = await commandHaber.ExecuteNonQueryAsync();

                            // Confirmar la transacción
                            transaction.Commit();
                            return true;
                        }
                        catch
                        {
                            // Revertir la transacción en caso de error
                            transaction.Rollback();
                            return false;
                        }
                    }
                }
            }
            catch
            {
                return false;
            }
        }

    }
}