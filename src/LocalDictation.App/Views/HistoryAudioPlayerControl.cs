using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Markup;
using System.Windows.Shapes;
using System.Windows.Threading;
using LocalDictation.App.Services;
using LocalDictation.Core;
using MaterialDesignThemes.Wpf;

namespace LocalDictation.App.Views;

/// <summary>
/// Standalone, windowless-renderable History player. The injected session may use
/// a fake output; construction, loading and rendering never create audio hardware.
/// </summary>
public sealed class HistoryAudioPlayerControl : UserControl,IDisposable
{
    readonly HistoryAudioPlayback playback;
    readonly Slider timeline=new RecordingTimeline(){Minimum=0,Maximum=1,MinHeight=48,SmallChange=1,LargeChange=5,IsMoveToPointEnabled=true,VerticalAlignment=VerticalAlignment.Center};
    readonly TextBlock time=Ui.Text("00:00 / 00:00",12),status=Ui.Text("Select a dictation",12);
    readonly AudioWaveformView waveform=new(){Height=40,Margin=new(28,0,28,0),Opacity=.28,VerticalAlignment=VerticalAlignment.Center};
    readonly Button play,pause,stop;
    readonly DispatcherTimer timer;
    bool updating,dragging,disposed,failed;

    public HistoryAudioPlayerControl():this(new HistoryAudioPlayback(wave=>new HistoryAudioOutput(wave))){}

    public HistoryAudioPlayerControl(HistoryAudioPlayback playback)
    {
        this.playback=playback??throw new ArgumentNullException(nameof(playback));
        var content=new StackPanel();
        var row=new Grid();row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});row.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        play=Ui.IconButton(PackIconKind.Play,"Play original recording",()=>Run(playback.Play));
        pause=Ui.IconButton(PackIconKind.Pause,"Pause playback",()=>Run(playback.Pause));
        stop=Ui.IconButton(PackIconKind.Stop,"Stop playback and return to start",()=>Run(playback.Stop));
        foreach(var button in new[]{play,pause,stop}){button.Margin=new(0);button.Width=button.Height=48;}
        play.SetResourceReference(Control.ForegroundProperty,"Accent");
        row.Children.Add(Ui.Row(play,pause,stop));
        status.Margin=new(12,0,8,0);status.VerticalAlignment=VerticalAlignment.Center;status.TextWrapping=TextWrapping.NoWrap;status.TextTrimming=TextTrimming.CharacterEllipsis;status.SetResourceReference(TextBlock.ForegroundProperty,"Muted");
        Grid.SetColumn(status,1);row.Children.Add(status);
        time.Margin=new(8,0,8,0);time.VerticalAlignment=VerticalAlignment.Center;time.TextWrapping=TextWrapping.NoWrap;time.SetResourceReference(TextBlock.ForegroundProperty,"Muted");
        AutomationProperties.SetName(time,"Playback elapsed and total time");Grid.SetColumn(time,2);row.Children.Add(time);content.Children.Add(row);
        timeline.SetResourceReference(FrameworkElement.StyleProperty,"MaterialDesign3.MaterialDesignSlider");
        timeline.SetResourceReference(Control.ForegroundProperty,"Accent");
        timeline.Background=Brushes.Transparent;timeline.Margin=new(4,0,4,0);
        AutomationProperties.SetName(timeline,"Recording playback position");
        AutomationProperties.SetHelpText(timeline,"Drag the thumb to seek. Left and Right move one second; Page Up and Page Down move five seconds.");
        timeline.ValueChanged+=(_,_)=>
        {
            if(updating||!playback.HasAudio)return;
            if(dragging)UpdatePosition();else Run(()=>playback.Seek(TimeSpan.FromSeconds(timeline.Value)));
        };
        timeline.AddHandler(Thumb.DragStartedEvent,new DragStartedEventHandler((_,_)=>dragging=true));
        timeline.AddHandler(Thumb.DragCompletedEvent,new DragCompletedEventHandler((_,_)=>
        {
            dragging=false;if(playback.HasAudio)Run(()=>playback.Seek(TimeSpan.FromSeconds(timeline.Value)));
        }));
        waveform.SetResourceReference(Control.ForegroundProperty,"Muted");waveform.SetResourceReference(AudioWaveformView.PlayedBrushProperty,"Accent");
        AutomationProperties.SetName(waveform,"Original recording energy waveform");
        var seekRow=new Grid{Height=48};seekRow.Children.Add(waveform);seekRow.Children.Add(timeline);content.Children.Add(seekRow);
        var card=Ui.Card(content);card.Padding=new(12,6,12,6);card.Margin=new(0);Content=card;
        timer=new DispatcherTimer(TimeSpan.FromMilliseconds(100),DispatcherPriority.Background,(_,_)=>RefreshPlayback(),Dispatcher);
        timer.Stop();Loaded+=(_,_)=>{if(!disposed)timer.Start();};Unloaded+=(_,_)=>timer.Stop();
        Update();
    }

    public event Action<string>? PlaybackError;

    /// <summary>Takes ownership of the decrypted original WAV and clears the previous selection.</summary>
    public void LoadAudio(byte[] wave){playback.Load(wave);dragging=false;failed=false;Update();}

    public void Clear(){dragging=false;failed=false;playback.Clear();Update();}

    public void RefreshPlayback()
    {
        if(disposed)return;
        try{playback.Refresh();Update();}
        catch{ShowError();}
    }

    void Run(Action action)
    {
        try{action();failed=false;Update();}
        catch{ShowError();}
    }

    void ShowError()
    {
        failed=true;Update();
        PlaybackError?.Invoke("Audio could not be played. Check your output device and saved recording.");
    }

    void Update()
    {
        updating=true;
        try
        {
            timeline.IsEnabled=playback.HasAudio;timeline.Maximum=Math.Max(.001,playback.Duration.TotalSeconds);
            if(!dragging)timeline.Value=playback.Position.TotalSeconds;
            play.IsEnabled=playback.HasAudio&&playback.State!=HistoryPlaybackState.Playing;
            pause.IsEnabled=playback.State==HistoryPlaybackState.Playing;
            stop.IsEnabled=playback.HasAudio&&(playback.State is HistoryPlaybackState.Playing or HistoryPlaybackState.Paused||playback.Position>TimeSpan.Zero);
            status.Text=failed?"Playback unavailable":playback.State switch
            {HistoryPlaybackState.Empty=>"Select a dictation",HistoryPlaybackState.Playing=>"Playing",HistoryPlaybackState.Paused=>"Paused",_=>"Ready"};
            waveform.Peaks=playback.Peaks;UpdatePosition();
        }
        finally{updating=false;}
    }

    void UpdatePosition()
    {
        var position=TimeSpan.FromSeconds(timeline.Value);
        time.Text=$"{position:mm\\:ss} / {playback.Duration:mm\\:ss}";
        waveform.Progress=playback.Duration>TimeSpan.Zero?position.TotalSeconds/playback.Duration.TotalSeconds:0;
    }

    public void Dispose()
    {
        if(disposed)return;disposed=true;timer.Stop();playback.Dispose();Update();
    }

    // Keep native Slider/Track seeking and automation. The thin track and circular
    // thumb share the center of a transparent 48-DIP interaction area.
    sealed class RecordingTimeline : Slider
    {
        public RecordingTimeline()
        {
            FocusVisualStyle=null; // The thumb template draws the visible focus ring.
            Template=(ControlTemplate)XamlReader.Parse("""
                <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                 xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="{x:Type Slider}">
                  <Grid Background="Transparent" MinHeight="48">
                    <Border Height="4" Margin="24,0" VerticalAlignment="Center" CornerRadius="2" Background="{DynamicResource Selected}"/>
                    <Track x:Name="PART_Track" Height="48" VerticalAlignment="Center"
                           Minimum="{TemplateBinding Minimum}" Maximum="{TemplateBinding Maximum}"
                           Value="{Binding Value, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}"
                           IsDirectionReversed="{TemplateBinding IsDirectionReversed}">
                      <Track.DecreaseRepeatButton>
                        <RepeatButton Command="{x:Static Slider.DecreaseLarge}" Focusable="False" IsTabStop="False">
                          <RepeatButton.Template>
                            <ControlTemplate TargetType="{x:Type RepeatButton}">
                              <Grid Background="Transparent"><Border Height="4" Margin="24,0,-24,0" VerticalAlignment="Center" CornerRadius="2" Background="{DynamicResource Accent}"/></Grid>
                            </ControlTemplate>
                          </RepeatButton.Template>
                        </RepeatButton>
                      </Track.DecreaseRepeatButton>
                      <Track.IncreaseRepeatButton>
                        <RepeatButton Command="{x:Static Slider.IncreaseLarge}" Focusable="False" IsTabStop="False">
                          <RepeatButton.Template><ControlTemplate TargetType="{x:Type RepeatButton}"><Grid Background="Transparent"/></ControlTemplate></RepeatButton.Template>
                        </RepeatButton>
                      </Track.IncreaseRepeatButton>
                      <Track.Thumb><Thumb Width="48" Height="48" Focusable="False" VerticalAlignment="Center"/></Track.Thumb>
                    </Track>
                  </Grid>
                  <ControlTemplate.Triggers><Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.38"/></Trigger></ControlTemplate.Triggers>
                </ControlTemplate>
                """);
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            if(VisualTreeHelper.GetChildrenCount(this)>0&&VisualTreeHelper.GetChild(this,0) is Panel root&&root.Background==null)
                root.Background=Brushes.Transparent;
            if(GetTemplateChild("PART_Track") is not Track {Thumb: {} thumb})return;
            thumb.Width=thumb.Height=48;thumb.Margin=new(0);thumb.Template=RoundThumb();
        }

        static ControlTemplate RoundThumb()
        {
            var template=new ControlTemplate(typeof(Thumb));
            var root=new FrameworkElementFactory(typeof(Grid));root.SetValue(Panel.BackgroundProperty,Brushes.Transparent);
            FrameworkElementFactory Circle(string name,double size,string brush,double opacity=1)
            {
                var circle=new FrameworkElementFactory(typeof(Ellipse),name);
                circle.SetValue(WidthProperty,size);circle.SetValue(HeightProperty,size);
                circle.SetValue(HorizontalAlignmentProperty,HorizontalAlignment.Center);circle.SetValue(VerticalAlignmentProperty,VerticalAlignment.Center);
                circle.SetResourceReference(Shape.FillProperty,brush);circle.SetValue(OpacityProperty,opacity);return circle;
            }
            root.AppendChild(Circle("State",40,"Accent",0));
            var focus=Circle("Focus",28,"Accent",0);focus.SetValue(Shape.FillProperty,Brushes.Transparent);
            focus.SetResourceReference(Shape.StrokeProperty,"Accent");focus.SetValue(Shape.StrokeThicknessProperty,2d);root.AppendChild(focus);
            var handle=Circle("Handle",20,"Accent");handle.SetResourceReference(Shape.StrokeProperty,"SurfaceContainer");
            handle.SetValue(Shape.StrokeThicknessProperty,2d);root.AppendChild(handle);template.VisualTree=root;
            template.Triggers.Add(new Trigger{Property=IsMouseOverProperty,Value=true,Setters={new Setter(OpacityProperty,.08,"State")}});
            template.Triggers.Add(new Trigger{Property=Thumb.IsDraggingProperty,Value=true,Setters={new Setter(OpacityProperty,.12,"State")}});
            template.Triggers.Add(new DataTrigger
            {
                Binding=new Binding(nameof(IsKeyboardFocusWithin)){RelativeSource=new(RelativeSourceMode.FindAncestor,typeof(Slider),1)},Value=true,
                Setters={new Setter(OpacityProperty,1d,"Focus")}
            });
            template.Triggers.Add(new Trigger
            {
                Property=IsEnabledProperty,Value=false,
                Setters={new Setter(OpacityProperty,.38,"Handle"),new Setter(OpacityProperty,0d,"State"),new Setter(OpacityProperty,0d,"Focus")}
            });
            return template;
        }
    }
}

/// <summary>Display-scaled RMS energy from the selected PCM, drawn behind the timeline.</summary>
public sealed class AudioWaveformView : Control
{
    public static readonly DependencyProperty PlayedBrushProperty=DependencyProperty.Register(nameof(PlayedBrush),typeof(Brush),typeof(AudioWaveformView),new FrameworkPropertyMetadata(null,FrameworkPropertyMetadataOptions.AffectsRender));
    public Brush? PlayedBrush{get=>(Brush?)GetValue(PlayedBrushProperty);set=>SetValue(PlayedBrushProperty,value);}
    IReadOnlyList<float> peaks=[];double progress;
    public IReadOnlyList<float> Peaks{get=>peaks;set{if(ReferenceEquals(peaks,value))return;peaks=value;InvalidateVisual();}}
    public double Progress{get=>progress;set{if(progress==value)return;progress=Math.Clamp(value,0,1);InvalidateVisual();}}
    public AudioWaveformView(){IsHitTestVisible=false;Focusable=false;ClipToBounds=true;}

    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        var width=ActualWidth;var height=ActualHeight;if(width<=0||height<=0)return;
        var unplayed=new Pen(Foreground,2){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round};
        if(peaks.Count==0){drawing.DrawLine(new Pen(Foreground,1),new(0,height/2),new(width,height/2));return;}
        var played=new Pen(PlayedBrush??Foreground,2){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round};
        var count=Math.Min(peaks.Count,Math.Max(1,(int)(width/4)));
        for(var i=0;i<count;i++)
        {
            var start=i*peaks.Count/count;var end=(i+1)*peaks.Count/count;float peak=0;
            for(var j=start;j<end;j++)peak=Math.Max(peak,peaks[j]);
            var x=(i+.5)*width/count;var half=Math.Max(.5,peak*(height-4)/2);
            drawing.DrawLine(x<=progress*width?played:unplayed,new(x,height/2-half),new(x,height/2+half));
        }
    }
}
