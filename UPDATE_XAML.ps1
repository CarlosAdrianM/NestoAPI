# Script para actualizar FacturarRutasPopup.xaml

$file = "C:/Users/Carlos/source/repos/Nesto/Modulos/PedidoVenta/PedidoVenta/Views/FacturarRutasPopup.xaml"
$content = Get-Content $file -Raw

# Buscar y reemplazar el StackPanel por ListBox dinámico
$oldBlock = @'
        <!-- Opciones de ruta -->
        <StackPanel Grid.Row="1" Margin="0,0,0,20">
            <RadioButton Content="Ruta propia (16, AT)"
                         IsChecked="{Binding EsRutaPropia}"
                         Margin="0,0,0,10"
                         FontSize="13"
                         GroupName="TipoRuta"/>
            <RadioButton Content="Rutas de agencias (FW, 00)"
                         IsChecked="{Binding EsRutasAgencias}"
                         FontSize="13"
                         GroupName="TipoRuta"/>
        </StackPanel>
'@

$newBlock = @'
        <!-- Opciones de ruta DINÁMICAS -->
        <ListBox Grid.Row="1"
                 ItemsSource="{Binding TiposRutaDisponibles}"
                 SelectedItem="{Binding TipoRutaSeleccionado}"
                 Margin="0,0,0,20"
                 BorderThickness="0"
                 Background="Transparent">
            <ListBox.ItemTemplate>
                <DataTemplate>
                    <RadioButton Content="{Binding DisplayText}"
                                 IsChecked="{Binding IsSelected, RelativeSource={RelativeSource AncestorType=ListBoxItem}}"
                                 GroupName="TipoRuta"
                                 FontSize="13"/>
                </DataTemplate>
            </ListBox.ItemTemplate>
            <ListBox.ItemContainerStyle>
                <Style TargetType="ListBoxItem">
                    <Setter Property="Template">
                        <Setter.Value>
                            <ControlTemplate TargetType="ListBoxItem">
                                <ContentPresenter Margin="0,0,0,10"/>
                            </ControlTemplate>
                        </Setter.Value>
                    </Setter>
                </Style>
            </ListBox.ItemContainerStyle>
        </ListBox>
'@

$content = $content.Replace($oldBlock, $newBlock)

[System.IO.File]::WriteAllText($file, $content)

Write-Host "XAML actualizado exitosamente!"
