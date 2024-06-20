﻿using ImGuiNET;
using Vintagestory.API.Common;

namespace CombatOverhaul.Animations;

public sealed class Animation
{
    public List<PLayerKeyFrame> PlayerKeyFrames { get; } = new();
    public List<ItemKeyFrame> ItemKeyFrames { get; } = new();
    public List<SoundFrame> SoundFrames { get; } = new();
    public TimeSpan TotalDuration => PlayerKeyFrames[^1].Time;
    public TimeSpan ItemAnimationStart { get; set; } = TimeSpan.Zero;
    public TimeSpan ItemAnimationEnd { get; set; } = TimeSpan.Zero;
    public bool Hold { get; set; } = false;

    public Animation(IEnumerable<PLayerKeyFrame> playerFrames, IEnumerable<ItemKeyFrame> itemFrames, IEnumerable<SoundFrame> soundFrames)
    {
        if (!playerFrames.Any()) throw new ArgumentException("Frames number should be at least 1");

        PlayerKeyFrames = playerFrames.ToList();
        ItemKeyFrames = itemFrames.ToList();
        SoundFrames = soundFrames.ToList();

        PlayerKeyFrames.Sort((x, y) => (int)(x.Time - y.Time).TotalMilliseconds);
        ItemKeyFrames.Sort((x, y) => (int)((x.DurationFraction - y.DurationFraction) * 1E6f));
        SoundFrames.Sort((x, y) => (int)((x.DurationFraction - y.DurationFraction) * 1E6f));

        ItemAnimationEnd = TotalDuration;
    }
    public Animation(IEnumerable<PLayerKeyFrame> playerFrames, IEnumerable<ItemKeyFrame> itemFrames)
    {
        if (!playerFrames.Any()) throw new ArgumentException("Frames number should be at least 1");

        PlayerKeyFrames = playerFrames.ToList();
        ItemKeyFrames = itemFrames.ToList();

        PlayerKeyFrames.Sort((x, y) => (int)(x.Time - y.Time).TotalMilliseconds);
        ItemKeyFrames.Sort((x, y) => (int)((x.DurationFraction - y.DurationFraction) * 1E6f));

        ItemAnimationEnd = TotalDuration;
    }
    public Animation(IEnumerable<PLayerKeyFrame> playerFrames)
    {
        if (!playerFrames.Any()) throw new ArgumentException("Frames number should be at least 1");

        PlayerKeyFrames = playerFrames.ToList();

        PlayerKeyFrames.Sort((x, y) => (int)(x.Time - y.Time).TotalMilliseconds);
        ItemKeyFrames.Sort((x, y) => (int)((x.DurationFraction - y.DurationFraction) * 1E6f));

        ItemAnimationEnd = TotalDuration;
    }
    public Animation(IEnumerable<PLayerKeyFrame> playerFrames, string itemAnimation, Shape itemShape)
    {
        if (!playerFrames.Any()) throw new ArgumentException("Frames number should be at least 1");

        PlayerKeyFrames = playerFrames.ToList();
        ItemKeyFrames = ItemKeyFrame.FromVanillaAnimation(itemAnimation, itemShape);

        PlayerKeyFrames.Sort((x, y) => (int)(x.Time - y.Time).TotalMilliseconds);
        ItemKeyFrames.Sort((x, y) => (int)((x.DurationFraction - y.DurationFraction) * 1E6f));

        ItemAnimationEnd = TotalDuration;
    }

    public static readonly Animation Zero = new(new PLayerKeyFrame[] { PLayerKeyFrame.Zero });

    public void PlaySounds(SoundsSynchronizerClient soundsManager, TimeSpan currentDuration, TimeSpan previousDuration)
    {
        foreach (SoundFrame frame in SoundFrames.Where(frame => frame.DurationFraction * TotalDuration > previousDuration && frame.DurationFraction * TotalDuration <= currentDuration))
        {
            soundsManager.Play(frame);
        }
    }
    public PlayerItemFrame Interpolate(PlayerItemFrame previousAnimationFrame, TimeSpan currentDuration)
    {
        if (Finished(currentDuration)) return new(PlayerKeyFrames[^1].Frame, ItemKeyFrames.Any() ? ItemKeyFrames[^1].Frame : null);

        PlayerFrame playerFrame = InterpolatePlayerFrame(previousAnimationFrame, currentDuration, out TimeSpan adjustedCurrentDuration);
        ItemFrame? itemFrame = InterpolateItemFrame(previousAnimationFrame, adjustedCurrentDuration);

        return new(playerFrame, itemFrame);
    }
    public bool Finished(TimeSpan currentDuration) => currentDuration >= TotalDuration;
    public void Edit(string title)
    {
        if (ImGui.Button($"Sort frames##{title}"))
        {
            PlayerKeyFrames.Sort((x, y) => (int)(x.Time - y.Time).TotalMilliseconds);
            ItemKeyFrames.Sort((x, y) => (int)((x.DurationFraction - y.DurationFraction) * 1E6f));
        }
        ImGui.SameLine();

        ImGui.Text($"Total duration: {(int)TotalDuration.TotalMilliseconds} ms");
        ImGui.SameLine();

        bool hold = Hold;
        ImGui.Checkbox($"Hold##{title}", ref hold);
        Hold = hold;

        ImGui.BeginTabBar($"##{title}tab");
        if (ImGui.BeginTabItem($"Player animation##{title}"))
        {
            EditPlayerAnimation(title + "player");

            ImGui.EndTabItem();
        }
        if (ItemKeyFrames.Any() && ImGui.BeginTabItem($"Item animation##{title}"))
        {
            int itemAnimationStart = (int)ItemAnimationStart.TotalMilliseconds;
            ImGui.DragInt($"Item animation start##{title}", ref itemAnimationStart);
            ItemAnimationStart = TimeSpan.FromMilliseconds(itemAnimationStart);

            int itemAnimationEnd = (int)ItemAnimationEnd.TotalMilliseconds;
            ImGui.DragInt($"Item animation end##{title}", ref itemAnimationEnd);
            ItemAnimationEnd = TimeSpan.FromMilliseconds(itemAnimationEnd);

            EditItemAnimation(title + "item");

            ImGui.EndTabItem();
        }
        if (ImGui.BeginTabItem($"Sounds##{title}"))
        {
            EditSoundFrames(title + "sounds");

            ImGui.EndTabItem();
        }
        ImGui.EndTabBar();



        if (ItemAnimationEnd > TotalDuration) ItemAnimationEnd = TotalDuration;
        if (ItemAnimationStart > ItemAnimationEnd) ItemAnimationStart = ItemAnimationEnd;
    }
    public override string ToString() => AnimationJson.FromAnimation(this).ToString();
    public PlayerItemFrame StillPlayerFrame(int playerFrame)
    {
        TimeSpan timestamp = PlayerKeyFrames[playerFrame].Time - TimeSpan.FromMilliseconds(1);
        if (timestamp < TimeSpan.Zero) timestamp = TimeSpan.Zero;


        return Interpolate(PlayerItemFrame.Zero, timestamp);
    }
    public PlayerItemFrame StillItemFrame(int itemFrame)
    {
        return StillFrame(ItemKeyFrames[itemFrame].DurationFraction);
    }
    public PlayerItemFrame StillFrame(float progress)
    {
        return Interpolate(PlayerItemFrame.Zero, progress * TotalDuration);
    }

    internal int _playerFrameIndex = 0;
    internal int _itemFrameIndex = 0;
    internal bool _playerFrameEdited = true;
    internal int _soundsFrameIndex = 0;

    private ItemFrame? InterpolateItemFrame(PlayerItemFrame previousAnimationFrame, TimeSpan currentDuration)
    {
        if (!ItemKeyFrames.Any()) return null;

        int nextItemKeyFrame;
        TimeSpan totalDurationWithEasing = ItemAnimationEnd - ItemAnimationStart;
        TimeSpan currentDurationWithEasing = currentDuration - ItemAnimationStart;
        float progress = Math.Clamp((float)(currentDurationWithEasing / totalDurationWithEasing), 0, 1);

        for (nextItemKeyFrame = 0; nextItemKeyFrame < ItemKeyFrames.Count; nextItemKeyFrame++)
        {
            if (ItemKeyFrames[nextItemKeyFrame].DurationFraction > progress) break;
        }

        if (nextItemKeyFrame >= ItemKeyFrames.Count) nextItemKeyFrame = ItemKeyFrames.Count - 1;

        float itemFrameProgress;
        if (nextItemKeyFrame == 0)
        {
            itemFrameProgress = progress / ItemKeyFrames[0].DurationFraction;
        }
        else
        {
            float previousFrameProgress = ItemKeyFrames[nextItemKeyFrame - 1].DurationFraction;
            float nextFrameProgress = ItemKeyFrames[nextItemKeyFrame].DurationFraction;
            float progressRange = nextFrameProgress - previousFrameProgress;
            itemFrameProgress = (progress - previousFrameProgress) / progressRange;
        }

        if (nextItemKeyFrame == 0)
        {
            ItemFrame previousFrame = previousAnimationFrame.Item ?? ItemFrame.Empty;
            return ItemKeyFrames[0].Interpolate(previousFrame, itemFrameProgress);
        }
        else
        {
            return ItemKeyFrames[nextItemKeyFrame].Interpolate(ItemKeyFrames[nextItemKeyFrame - 1].Frame, itemFrameProgress);
        }
    }
    private PlayerFrame InterpolatePlayerFrame(PlayerItemFrame previousAnimationFrame, TimeSpan currentDuration, out TimeSpan adjustedCurrentDuration)
    {
        int nextPlayerKeyFrame;
        for (nextPlayerKeyFrame = 0; nextPlayerKeyFrame < PlayerKeyFrames.Count; nextPlayerKeyFrame++)
        {
            if (PlayerKeyFrames[nextPlayerKeyFrame].Time > currentDuration) break;
        } 

        if (nextPlayerKeyFrame == 0)
        {
            float frameProgress = (float)(currentDuration / PlayerKeyFrames[nextPlayerKeyFrame].Time);
            adjustedCurrentDuration = currentDuration * EasingFunctions.Get(PlayerKeyFrames[nextPlayerKeyFrame].EasingFunction).Invoke(frameProgress);

            return PlayerKeyFrames[0].Interpolate(previousAnimationFrame.Player, frameProgress);
        }
        else
        {
            TimeSpan frameDuration = PlayerKeyFrames[nextPlayerKeyFrame].Time - PlayerKeyFrames[nextPlayerKeyFrame - 1].Time;
            float frameProgress = (float)((currentDuration - PlayerKeyFrames[nextPlayerKeyFrame - 1].Time) / frameDuration);
            adjustedCurrentDuration = PlayerKeyFrames[nextPlayerKeyFrame - 1].Time + frameDuration * EasingFunctions.Get(PlayerKeyFrames[nextPlayerKeyFrame].EasingFunction).Invoke(frameProgress);

            return PlayerKeyFrames[nextPlayerKeyFrame].Interpolate(PlayerKeyFrames[nextPlayerKeyFrame - 1].Frame, frameProgress);
        }
    }
    private void EditPlayerAnimation(string title)
    {
        if (_playerFrameIndex >= PlayerKeyFrames.Count) _playerFrameIndex = PlayerKeyFrames.Count - 1;
        if (_playerFrameIndex < 0) _playerFrameIndex = 0;

        if (PlayerKeyFrames.Count > 0)
        {
            if (ImGui.Button($"Remove##{title}"))
            {
                PlayerKeyFrames.RemoveAt(_playerFrameIndex);
            }
            ImGui.SameLine();
            if (ImGui.Button($"Duplicate##{title}"))
            {
                PlayerKeyFrames.Insert(_playerFrameIndex + 1, PlayerKeyFrames[_playerFrameIndex]);
                _playerFrameIndex++;
            }
            ImGui.SameLine();
        }

        if (_playerFrameIndex >= PlayerKeyFrames.Count) _playerFrameIndex = PlayerKeyFrames.Count - 1;
        if (_playerFrameIndex < 0) _playerFrameIndex = 0;

        if (ImGui.Button($"Insert##{title}"))
        {
            PlayerKeyFrames.Insert(_playerFrameIndex, new(PlayerFrame.Zero, TimeSpan.Zero, EasingFunctionType.Linear));
        }

        if (PlayerKeyFrames.Count > 0) ImGui.SliderInt($"Key frame##{title}", ref _playerFrameIndex, 0, PlayerKeyFrames.Count - 1);

        if (PlayerKeyFrames.Count > 0)
        {
            PLayerKeyFrame frame = PlayerKeyFrames[_playerFrameIndex].Edit(title);
            PlayerKeyFrames[_playerFrameIndex] = frame;
        }

        _playerFrameEdited = true;
    }
    private void EditItemAnimation(string title)
    {
        if (_itemFrameIndex >= ItemKeyFrames.Count) _itemFrameIndex = ItemKeyFrames.Count - 1;
        if (_itemFrameIndex < 0) _itemFrameIndex = 0;

        if (ItemKeyFrames.Count > 0)
        {
            ImGui.SliderInt($"Key frame##{title}", ref _itemFrameIndex, 0, ItemKeyFrames.Count - 1);

            ItemKeyFrame frame = ItemKeyFrames[_itemFrameIndex].Edit(title, TotalDuration);
            ItemKeyFrames[_itemFrameIndex] = frame;
        }

        _playerFrameEdited = false;
    }
    private void EditSoundFrames(string title)
    {
        if (ImGui.Button($"Add##{title}"))
        {
            SoundFrames.Add(new("", 0));
        }
        ImGui.SameLine();

        if (_soundsFrameIndex >= SoundFrames.Count) _soundsFrameIndex = SoundFrames.Count - 1;
        if (_soundsFrameIndex < 0) _soundsFrameIndex = 0;

        bool canRemove = SoundFrames.Any();
        if (!canRemove) ImGui.BeginDisabled();
        if (ImGui.Button($"Remove##{title}"))
        {
            SoundFrames.RemoveAt(_soundsFrameIndex);
        }
        if (!canRemove) ImGui.EndDisabled();

        ImGui.ListBox($"Sounds##{title}", ref _soundsFrameIndex, SoundFrames.Select(element => element.Code).ToArray(), SoundFrames.Count);

        ImGui.Separator();

        if (_soundsFrameIndex < SoundFrames.Count)
        {
            SoundFrames[_soundsFrameIndex] = SoundFrames[_soundsFrameIndex].Edit(title);
        }
    }
}

public sealed class AnimationJson
{
    public bool Hold { get; set; } = false;
    public PLayerKeyFrameJson[] PlayerKeyFrames { get; set; } = Array.Empty<PLayerKeyFrameJson>();
    public ItemKeyFrameJson[] ItemKeyFrames { get; set; } = Array.Empty<ItemKeyFrameJson>();
    public SoundFrameJson[] SoundFrames { get; set; } = Array.Empty<SoundFrameJson>();
    public int ItemAnimationStart { get; set; }
    public int ItemAnimationEnd { get; set; }

    public Animation ToAnimation()
    {
        return new(PlayerKeyFrames.Select(element => element.ToKeyFrame()), ItemKeyFrames.Select(element => element.ToKeyFrame()), SoundFrames.Select(element => element.ToSoundFrame()))
        {
            Hold = Hold,
            ItemAnimationStart = TimeSpan.FromMilliseconds(ItemAnimationStart),
            ItemAnimationEnd = TimeSpan.FromMilliseconds(ItemAnimationEnd)
        };
    }

    public static AnimationJson FromAnimation(Animation animation)
    {
        return new()
        {
            Hold = animation.Hold,
            PlayerKeyFrames = animation.PlayerKeyFrames.Select(PLayerKeyFrameJson.FromKeyFrame).ToArray(),
            ItemKeyFrames = animation.ItemKeyFrames.Select(ItemKeyFrameJson.FromKeyFrame).ToArray(),
            SoundFrames = animation.SoundFrames.Select(SoundFrameJson.FromSoundFrame).ToArray(),
            ItemAnimationStart = (int)animation.ItemAnimationStart.TotalMilliseconds,
            ItemAnimationEnd = (int)animation.ItemAnimationEnd.TotalMilliseconds
        };
    }

    public override string ToString() => JsonUtil.ToPrettyString(this);
}

public sealed class SoundFrameJson
{
    public string Code { get; set; } = "";
    public float DurationFraction { get; set; }
    public bool RandomizePitch { get; set; }
    public float Range { get; set; }
    public float Volume { get; set; }
    public bool Synchronize { get; set; }

    public SoundFrame ToSoundFrame()
    {
        return new(Code, DurationFraction, RandomizePitch, Range, Volume, Synchronize);
    }

    public static SoundFrameJson FromSoundFrame(SoundFrame frame)
    {
        return new()
        {
            Code = frame.Code,
            DurationFraction = frame.DurationFraction,
            RandomizePitch = frame.RandomizePitch,
            Range = frame.Range,
            Volume = frame.Volume,
            Synchronize = frame.Synchronize
        };
    }
}

public sealed class ItemKeyFrameJson
{
    public float DurationFraction { get; set; }
    public string EasingFunction { get; set; } = "Linear";
    public Dictionary<string, float[]> Elements { get; set; } = new();

    public ItemKeyFrame ToKeyFrame()
    {
        EasingFunctionType function = Enum.Parse<EasingFunctionType>(EasingFunction);

        return new(
                new ItemFrame(Elements.ToDictionary(entry => entry.Key, entry => new AnimationElement(entry.Value))),
                DurationFraction,
                function
            );
    }

    public static ItemKeyFrameJson FromKeyFrame(ItemKeyFrame frame)
    {
        ItemKeyFrameJson result = new()
        {
            DurationFraction = frame.DurationFraction,
            EasingFunction = frame.EasingFunction.ToString(),
            Elements = frame.Frame.Elements.ToDictionary(entry => entry.Key, entry => entry.Value.ToArray())
        };

        return result;
    }
}

public sealed class PLayerKeyFrameJson
{
    public float EasingTime { get; set; } = 0;
    public string EasingFunction { get; set; } = "Linear";
    public bool DetachedAnchor { get; set; } = false;
    public bool SwitchArms { get; set; } = false;
    public bool PitchFollow { get; set; } = false;
    public float FOVMultiplier { get; set; } = 1;
    public float BobbingAmplitude { get; set; } = 1;
    public Dictionary<string, float[]> Elements { get; set; } = new();

    public PLayerKeyFrame ToKeyFrame()
    {
        TimeSpan time = TimeSpan.FromMilliseconds(EasingTime);
        EasingFunctionType function = Enum.Parse<EasingFunctionType>(EasingFunction);

        RightHandFrame? rightHand = null;
        if (Elements.ContainsKey("ItemAnchor") || Elements.ContainsKey("LowerArmR") || Elements.ContainsKey("UpperArmR"))
        {
            rightHand = new(
                Elements.ContainsKey("ItemAnchor") ? new AnimationElement(Elements["ItemAnchor"]) : AnimationElement.Zero,
                Elements.ContainsKey("LowerArmR") ? new AnimationElement(Elements["LowerArmR"]) : AnimationElement.Zero,
                Elements.ContainsKey("UpperArmR") ? new AnimationElement(Elements["UpperArmR"]) : AnimationElement.Zero
                );
        }

        LeftHandFrame? leftHand = null;
        if (Elements.ContainsKey("ItemAnchorL") || Elements.ContainsKey("LowerArmL") || Elements.ContainsKey("UpperArmL"))
        {
            leftHand = new(
                Elements.ContainsKey("ItemAnchorL") ? new AnimationElement(Elements["ItemAnchorL"]) : AnimationElement.Zero,
                Elements.ContainsKey("LowerArmL") ? new AnimationElement(Elements["LowerArmL"]) : AnimationElement.Zero,
                Elements.ContainsKey("UpperArmL") ? new AnimationElement(Elements["UpperArmL"]) : AnimationElement.Zero
                );
        }

        AnimationElement? torso = Elements.ContainsKey("UpperTorso") ? new(Elements["UpperTorso"]) : null;
        AnimationElement? anchor = Elements.ContainsKey("DetachedAnchor") ? new(Elements["DetachedAnchor"]) : null;

        float pitch = PitchFollow ? PlayerFrame.PerfectPitchFollow : PlayerFrame.DefaultPitchFollow;

        PlayerFrame frame = new(rightHand, leftHand, torso, anchor, DetachedAnchor, SwitchArms, pitch, FOVMultiplier, BobbingAmplitude);

        return new(
            frame,
            time,
            function
            );
    }

    public static PLayerKeyFrameJson FromKeyFrame(PLayerKeyFrame frame)
    {
        PLayerKeyFrameJson result = new()
        {
            EasingTime = (float)frame.Time.TotalMilliseconds,
            EasingFunction = frame.EasingFunction.ToString(),
            DetachedAnchor = frame.Frame.DetachedAnchor,
            SwitchArms = frame.Frame.SwitchArms,
            PitchFollow = Math.Abs(frame.Frame.PitchFollow - PlayerFrame.PerfectPitchFollow) < PlayerFrame.Epsilon,
            FOVMultiplier = frame.Frame.FovMultiplier,
            BobbingAmplitude = frame.Frame.BobbingAmplitude
        };

        if (frame.Frame.RightHand != null)
        {
            RightHandFrame rightHand = frame.Frame.RightHand.Value;

            result.Elements.Add("ItemAnchor", rightHand.ItemAnchor.ToArray());
            result.Elements.Add("LowerArmR", rightHand.LowerArmR.ToArray());
            result.Elements.Add("UpperArmR", rightHand.UpperArmR.ToArray());
        }

        if (frame.Frame.LeftHand != null)
        {
            LeftHandFrame leftHand = frame.Frame.LeftHand.Value;

            result.Elements.Add("ItemAnchorL", leftHand.ItemAnchorL.ToArray());
            result.Elements.Add("LowerArmL", leftHand.LowerArmL.ToArray());
            result.Elements.Add("UpperArmL", leftHand.UpperArmL.ToArray());
        }

        if (frame.Frame.UpperTorso != null) result.Elements.Add("UpperTorso", frame.Frame.UpperTorso.Value.ToArray());
        if (frame.Frame.DetachedAnchorFrame != null)  result.Elements.Add("DetachedAnchor", frame.Frame.DetachedAnchorFrame.Value.ToArray());

        return result;
    }
}